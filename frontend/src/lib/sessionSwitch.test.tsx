/**
 * Regression: switching accounts from the header quick-switcher must refetch tenant-scoped
 * data immediately. Previously the cache was cleared *after* the new session was stored,
 * which cancelled the new tenant's in-flight `/tenant/current` and left the dashboard
 * stuck on its skeleton until a full page reload.
 */
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { ACME_CURRENT, loginResponse, mockFetch, renderApp, signInAs, USERS, type MockCall } from '../test/utils';
import { useAuthStore } from '../stores/auth';
import type { AuthUser } from './types';

const TENANT_IDS: Record<string, string> = {
  acme: ACME_CURRENT.tenantContext.tenantId,
  beta: 'c2f5a0de-0000-4000-8000-0000000000be',
  globex: 'd3a6b1ef-0000-4000-8000-0000000000e1',
};

const tenantUser = (id: string, email: string, fullName: string, role: AuthUser['role'], subdomain: string): AuthUser => ({
  id,
  email,
  fullName,
  role,
  tenantId: TENANT_IDS[subdomain]!,
  subdomain,
});

const ACCOUNTS: Record<string, AuthUser> = {
  'admin@globex.com': tenantUser('33333333-0000-0000-0000-000000000001', 'admin@globex.com', 'Globex Admin', 'TenantAdmin', 'globex'),
  'admin@beta.com': tenantUser('44444444-0000-0000-0000-000000000001', 'admin@beta.com', 'Beta Admin', 'TenantAdmin', 'beta'),
  'admin@acme.com': USERS.acmeAdmin!,
  'agent@acme.com': tenantUser('22222222-2222-2222-2222-222222222222', 'agent@acme.com', 'Acme Agent', 'Agent', 'acme'),
};

function tenantCurrent(subdomain: string) {
  const dedicated = subdomain === 'globex';
  return {
    ...ACME_CURRENT,
    tenantContext: {
      ...ACME_CURRENT.tenantContext,
      tenantId: TENANT_IDS[subdomain]!,
      subdomain,
      planTier: subdomain === 'beta' ? 'Free' : dedicated ? 'Enterprise' : 'Pro',
      isolationStrategy: dedicated ? 'DedicatedDatabase' : 'SharedDatabase',
    },
    activeDatabase: dedicated ? `nexusdesk_tenant_${subdomain}` : 'nexusdesk_shared',
    rlsSessionTenantId: TENANT_IDS[subdomain]!,
    memberships: [],
    membershipsCount: 0,
  };
}

/** Backend that answers per account / per `X-Tenant-Subdomain`, with a small network delay. */
function mockBackend() {
  const delay = () => new Promise((r) => setTimeout(r, 20));
  const { fn } = mockFetch({
    'POST /api/v1/auth/login': (call: MockCall) => {
      const user = ACCOUNTS[(call.body as { email: string }).email];
      return user ? { body: loginResponse(user) } : { status: 400, body: { title: 'Authentication Failed', status: 400 } };
    },
    'GET /api/v1/tenant/current': (call: MockCall) => {
      const sub = call.headers.get('X-Tenant-Subdomain');
      return sub ? { body: tenantCurrent(sub) } : { status: 404, body: { title: 'Not Found', status: 404 } };
    },
  });
  const original = fn.getMockImplementation()!;
  fn.mockImplementation(async (...args) => {
    await delay();
    return original(...args);
  });
  return fn;
}

async function switchAccount(email: string) {
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Account menu' }));
  await user.click(screen.getByRole('button', { name: new RegExp(email.replace(/[.@]/g, '\\$&')) }));
}

describe('quick account switch refreshes tenant-scoped queries', () => {
  it('globex admin → beta admin renders beta data from a fresh /tenant/current without a reload', async () => {
    const fetchFn = mockBackend();
    signInAs(ACCOUNTS['admin@globex.com']!);
    renderApp('/dashboard');

    expect(await screen.findByText('db:nexusdesk_tenant_globex')).toBeInTheDocument();
    expect(screen.getByTestId('active-tenant')).toHaveTextContent('globex');

    await switchAccount('admin@beta.com');

    await waitFor(() => expect(useAuthStore.getState().user?.email).toBe('admin@beta.com'));
    // Header badge and the dashboard isolation card both show beta's live context.
    expect(await screen.findByText('db:nexusdesk_shared')).toBeInTheDocument();
    expect(screen.getByTestId('active-tenant')).toHaveTextContent('beta');
    expect(await screen.findByText('nexusdesk_shared')).toBeInTheDocument();
    expect(screen.queryByText('db:nexusdesk_tenant_globex')).not.toBeInTheDocument();

    // The refetch went out with beta's tenant header and beta's token.
    const betaCalls = fetchFn.mock.calls.filter(([url, init]) => {
      const h = new Headers(init?.headers);
      return String(url).includes('/api/v1/tenant/current') && h.get('X-Tenant-Subdomain') === 'beta';
    });
    expect(betaCalls.length).toBeGreaterThan(0);
    expect(new Headers(betaCalls.at(-1)![1]?.headers).get('Authorization')).toBe('Bearer token-TenantAdmin');
  });

  it('switching between two users of the same tenant still refetches /tenant/current', async () => {
    const fetchFn = mockBackend();
    signInAs(ACCOUNTS['admin@acme.com']!);
    renderApp('/dashboard');
    expect(await screen.findByText('db:nexusdesk_shared')).toBeInTheDocument();

    const tenantCalls = () => fetchFn.mock.calls.filter(([url]) => String(url).includes('/api/v1/tenant/current')).length;
    const before = tenantCalls();

    await switchAccount('agent@acme.com');

    await waitFor(() => expect(useAuthStore.getState().user?.email).toBe('agent@acme.com'));
    await waitFor(() => expect(tenantCalls()).toBeGreaterThan(before));
    expect(await screen.findByText('db:nexusdesk_shared')).toBeInTheDocument();
    expect(within(screen.getByRole('banner')).getByText('Acme Agent')).toBeInTheDocument();
  });
});
