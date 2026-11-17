import { describe, expect, it, vi } from 'vitest';
import { ApiProblemError, api, parseProblemDetails, setUnauthorizedHandler } from './api';
import { useAuthStore } from '../stores/auth';
import { useToastStore } from '../stores/toast';
import { USERS, loginResponse, mockFetch, signInAs } from '../test/utils';

describe('API client headers', () => {
  it('injects X-Tenant-Subdomain and Authorization from the auth store', async () => {
    signInAs(USERS.acmeAdmin);
    const { calls } = mockFetch({ 'GET /api/v1/users': { body: [] } });

    await api.get('/api/v1/users');

    expect(calls).toHaveLength(1);
    expect(calls[0].headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect(calls[0].headers.get('Authorization')).toBe('Bearer token-TenantAdmin');
  });

  it('follows the active tenant when it is switched', async () => {
    signInAs(USERS.superAdmin);
    const { calls } = mockFetch({ 'GET /api/v1/tenant/current': { body: {} } });

    await api.get('/api/v1/tenant/current');
    useAuthStore.getState().setTenantSubdomain('Globex ');
    await api.get('/api/v1/tenant/current');

    expect(calls[0].headers.has('X-Tenant-Subdomain')).toBe(false); // SuperAdmin = platform scope
    expect(calls[1].headers.get('X-Tenant-Subdomain')).toBe('globex');
  });

  it('lets a call override or omit the tenant header and skip auth', async () => {
    signInAs(USERS.acmeAdmin);
    const { calls } = mockFetch({ 'POST /api/v1/auth/login': { body: {} }, 'GET /api/v1/platform/tenants': { body: {} } });

    await api.post('/api/v1/auth/login', { email: 'a', password: 'b' }, { tenant: 'beta', auth: false });
    await api.get('/api/v1/platform/tenants', { tenant: null });

    expect(calls[0].headers.get('X-Tenant-Subdomain')).toBe('beta');
    expect(calls[0].headers.has('Authorization')).toBe(false);
    expect(calls[0].headers.get('Content-Type')).toBe('application/json');
    expect(calls[0].body).toEqual({ email: 'a', password: 'b' });
    expect(calls[1].headers.has('X-Tenant-Subdomain')).toBe(false);
  });

  it('serialises query parameters and drops empty values', async () => {
    const { calls } = mockFetch({ 'GET /api/v1/platform/tenants': { body: {} } });
    await api.get('/api/v1/platform/tenants', { query: { page: 2, pageSize: 20, search: '', status: undefined } });
    expect(calls[0].url).toBe('/api/v1/platform/tenants?page=2&pageSize=20');
  });
});

describe('RFC 7807 ProblemDetails parsing', () => {
  it('maps title/detail/status and prefers `code` over `errorCode`', () => {
    const err = parseProblemDetails(422, {
      type: 'https://nexusdesk.io/errors/plan-agent-limit-exceeded',
      title: 'Plan Agent Limit Exceeded',
      status: 422,
      detail: "Tenant 'acme' on plan 'Pro' has reached the maximum quota of 10 active agents.",
      instance: '/api/v1/users/agents',
      code: 'PLAN_AGENT_LIMIT_EXCEEDED',
      errorCode: 'IGNORED',
      traceId: '00-abc-01',
    });
    expect(err).toBeInstanceOf(ApiProblemError);
    expect(err.status).toBe(422);
    expect(err.title).toBe('Plan Agent Limit Exceeded');
    expect(err.code).toBe('PLAN_AGENT_LIMIT_EXCEEDED');
    expect(err.instance).toBe('/api/v1/users/agents');
    expect(err.traceId).toBe('00-abc-01');
    expect(err.userMessage).toMatch(/maximum quota of 10/);
  });

  it('falls back to `errorCode` when `code` is absent', () => {
    expect(parseProblemDetails(403, { title: 'Suspended', errorCode: 'TENANT_SUSPENDED' }).code).toBe('TENANT_SUSPENDED');
  });

  it('flattens ASP.NET validation errors into the detail', () => {
    const err = parseProblemDetails(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { Subdomain: ['Subdomain is required.'], AdminEmail: ['Email is invalid.'] },
    });
    expect(err.validationErrors?.Subdomain).toEqual(['Subdomain is required.']);
    expect(err.userMessage).toBe('Subdomain is required. Email is invalid.');
  });

  it('handles empty and non-JSON bodies', () => {
    const empty = parseProblemDetails(503, undefined, 'Service Unavailable');
    expect(empty.title).toBe('Service Unavailable');
    expect(empty.detail).toBeUndefined();
    const text = parseProblemDetails(500, 'boom');
    expect(text.title).toBe('Server Error');
    expect(text.detail).toBe('boom');
  });

  it('throws ApiProblemError from a request and shows an error toast', async () => {
    signInAs(USERS.acmeAdmin);
    mockFetch({
      'POST /api/v1/users/agents': {
        status: 422,
        body: { title: 'Plan Agent Limit Exceeded', status: 422, detail: 'Quota reached.', code: 'PLAN_AGENT_LIMIT_EXCEEDED' },
      },
    });

    const err = await api.post('/api/v1/users/agents', {}).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(ApiProblemError);
    expect((err as ApiProblemError).code).toBe('PLAN_AGENT_LIMIT_EXCEEDED');
    const toasts = useToastStore.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0]).toMatchObject({ tone: 'error', title: 'Plan Agent Limit Exceeded', description: 'Quota reached.' });
    expect(toasts[0].meta).toContain('PLAN_AGENT_LIMIT_EXCEEDED');
  });

  it('does not toast when the call is silent', async () => {
    mockFetch({ 'GET /x': { status: 404, body: { title: 'Nope' } } });
    await expect(api.get('/x', { silent: true })).rejects.toBeInstanceOf(ApiProblemError);
    expect(useToastStore.getState().toasts).toHaveLength(0);
  });
});

describe('401 handling', () => {
  it('refreshes via /connect/token and replays the request', async () => {
    signInAs(USERS.acmeAdmin);
    let usersCalls = 0;
    const { calls } = mockFetch({
      'GET /api/v1/users': () => (++usersCalls === 1 ? { status: 401, body: { title: 'Unauthorized' } } : { body: ['ok'] }),
      'POST /connect/token': { body: { access_token: 'fresh', token_type: 'Bearer', expires_in: 3600, refresh_token: 'r2' } },
    });

    await expect(api.get('/api/v1/users')).resolves.toEqual(['ok']);

    const refresh = calls.find((c) => c.url.endsWith('/connect/token'))!;
    expect(refresh.body).toEqual({ grant_type: 'refresh_token', refresh_token: 'refresh-TenantAdmin', client_id: 'nexusdesk-spa' });
    expect(calls.at(-1)!.headers.get('Authorization')).toBe('Bearer fresh');
    expect(useAuthStore.getState().refreshToken).toBe('r2');
  });

  it('logs out and calls the unauthorized handler when refresh fails', async () => {
    useAuthStore.getState().setSession(loginResponse(USERS.acmeAdmin), 'acme');
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    mockFetch({
      'GET /api/v1/users': { status: 401, body: { title: 'Unauthorized' } },
      'POST /connect/token': { status: 400, body: { error: 'invalid_grant' } },
    });

    await expect(api.get('/api/v1/users')).rejects.toMatchObject({ status: 401 });
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(handler).toHaveBeenCalledOnce();
  });
});
