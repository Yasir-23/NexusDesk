import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { useAuthStore } from '../../stores/auth';
import { ACME_CURRENT, USERS, loginResponse, mockFetch, renderApp, signInAs } from '../../test/utils';

const TENANTS_PAGE = { page: 1, pageSize: 100, totalCount: 0, items: [] };

describe('Login flow', () => {
  it('signs a tenant admin in with the selected tenant header and redirects to /dashboard', async () => {
    const user = userEvent.setup();
    const { find } = mockFetch({
      'POST /api/v1/auth/login': { body: loginResponse(USERS.acmeAdmin) },
      'GET /api/v1/tenant/current': { body: ACME_CURRENT },
    });
    renderApp('/login');

    await user.click(screen.getByRole('radio', { name: 'acme' }));
    await user.type(screen.getByLabelText('Email'), 'admin@acme.com');
    await user.type(screen.getByLabelText('Password'), 'Password123!');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/dashboard'));
    const [loginCall] = find('POST', '/api/v1/auth/login');
    expect(loginCall.headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect(loginCall.body).toEqual({ email: 'admin@acme.com', password: 'Password123!' });
    expect(useAuthStore.getState()).toMatchObject({ accessToken: 'token-TenantAdmin', tenantSubdomain: 'acme' });

    // Header isolation badge is driven by /tenant/current
    expect(await screen.findByText('db:nexusdesk_shared')).toBeInTheDocument();
  });

  it('quick-logs the SuperAdmin in at platform scope and lands on the tenant directory', async () => {
    const user = userEvent.setup();
    const { find } = mockFetch({
      'POST /api/v1/auth/login': { body: loginResponse(USERS.superAdmin) },
      'GET /api/v1/platform/tenants': { body: TENANTS_PAGE },
    });
    renderApp('/login');

    await user.click(screen.getByRole('button', { name: 'Quick login as superadmin@nexusdesk.com' }));

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/platform/tenants'));
    expect(find('POST', '/api/v1/auth/login')[0].headers.has('X-Tenant-Subdomain')).toBe(false);
    expect(useAuthStore.getState().tenantSubdomain).toBeNull();
    expect(screen.getByTestId('active-tenant')).toHaveTextContent('platform');
  });

  it('routes a customer quick-login to the portal', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /api/v1/auth/login': { body: loginResponse(USERS.acmeCustomer) },
      'GET /api/v1/tenant/current': { body: ACME_CURRENT },
    });
    renderApp('/login');

    await user.click(screen.getByRole('button', { name: 'Quick login as customer@acme.com' }));
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/portal'));
  });

  it('shows ProblemDetails inline when credentials are rejected', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /api/v1/auth/login': {
        status: 400,
        body: { title: 'Authentication Failed', status: 400, detail: 'Invalid email or password.' },
      },
    });
    renderApp('/login');

    await user.type(screen.getByLabelText('Email'), 'admin@acme.com');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Authentication Failed')).toBeInTheDocument();
    expect(screen.getByText('Invalid email or password.')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/login');
    expect(useAuthStore.getState().accessToken).toBeNull();
  });
});

describe('Role-based route guards', () => {
  it('sends anonymous users to /login', () => {
    mockFetch({});
    renderApp('/platform/tenants');
    expect(screen.getByTestId('location')).toHaveTextContent('/login');
  });

  it('bounces a customer away from the SuperAdmin console to their portal', async () => {
    signInAs(USERS.acmeCustomer);
    mockFetch({ 'GET /api/v1/tenant/current': { body: ACME_CURRENT } });
    renderApp('/platform/tenants');
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/portal'));
  });

  it('bounces a tenant admin away from /platform/system to /dashboard', async () => {
    signInAs(USERS.acmeAdmin);
    mockFetch({ 'GET /api/v1/tenant/current': { body: ACME_CURRENT } });
    renderApp('/platform/system');
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/dashboard'));
  });

  it('shows only role-appropriate navigation', async () => {
    signInAs(USERS.superAdmin);
    mockFetch({ 'GET /api/v1/platform/tenants': { body: TENANTS_PAGE } });
    renderApp('/');
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/platform/tenants'));
    const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0];
    expect(nav).toHaveTextContent('Tenant Directory');
    expect(nav).toHaveTextContent('System Health');
    expect(nav).not.toHaveTextContent('SLA Policies');
    expect(nav).not.toHaveTextContent('My Requests');
  });
});
