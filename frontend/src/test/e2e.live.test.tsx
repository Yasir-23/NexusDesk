/**
 * Live end-to-end checks: renders the real app and talks to the running backend through
 * the Vite dev proxy (no mocks). Run with `npm run test:live` while both are up:
 *   - NexusDesk.Api on http://localhost:56100
 *   - Vite dev server on http://localhost:5173
 * Read-only: nothing is created, suspended or modified.
 */
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { API_BASE_URL } from '../lib/api';
import { useAuthStore } from '../stores/auth';
import { renderApp } from './utils';

const TIMEOUT = { timeout: 15_000 };

describe(`live backend via ${API_BASE_URL}`, () => {
  it('SuperAdmin quick-login lists tenants from GET /api/v1/platform/tenants', async () => {
    const user = userEvent.setup();
    renderApp('/login');

    await user.click(screen.getByRole('button', { name: 'Quick login as superadmin@nexusdesk.com' }));

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/platform/tenants'), TIMEOUT);
    expect(useAuthStore.getState().user?.role).toBe('SuperAdmin');
    expect(useAuthStore.getState().tenantSubdomain).toBeNull();

    const acme = await screen.findByTestId('tenant-row-acme', undefined, TIMEOUT);
    expect(within(acme).getByText('Acme Corporation')).toBeInTheDocument();
    expect(within(acme).getByText('Shared · RLS')).toBeInTheDocument();
    const globex = screen.getByTestId('tenant-row-globex');
    expect(within(globex).getByText('Enterprise')).toBeInTheDocument();
    expect(within(globex).getByText('Dedicated DB')).toBeInTheDocument();
  }, 30_000);

  it('SuperAdmin switches tenant scope and the header badge shows the dedicated globex database', async () => {
    const user = userEvent.setup();
    renderApp('/login');
    await user.click(screen.getByRole('button', { name: 'Quick login as superadmin@nexusdesk.com' }));
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/platform/tenants'), TIMEOUT);

    await user.click(screen.getByRole('button', { name: 'Tenant context' }));
    const switcher = screen.getByRole('group', { name: 'Switch tenant scope' });
    await user.click(within(switcher).getByRole('button', { name: /Globex Global/ }));

    expect(await screen.findByText('db:nexusdesk_tenant_globex', undefined, TIMEOUT)).toBeInTheDocument();
    expect(screen.getByTestId('active-tenant')).toHaveTextContent('globex');
  }, 30_000);

  it('Acme TenantAdmin login shows the shared-database RLS isolation badge from /api/v1/tenant/current', async () => {
    const user = userEvent.setup();
    renderApp('/login');

    await user.click(screen.getByRole('radio', { name: 'acme' }));
    await user.type(screen.getByLabelText('Email'), 'admin@acme.com');
    await user.type(screen.getByLabelText('Password'), 'Password123!');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/dashboard'), TIMEOUT);
    expect(await screen.findByText('db:nexusdesk_shared', undefined, TIMEOUT)).toBeInTheDocument();
    expect(screen.getByTestId('active-tenant')).toHaveTextContent('acme');
    // Dashboard isolation card from the same live response
    expect(await screen.findByText('nexusdesk_shared', undefined, TIMEOUT)).toBeInTheDocument();
  }, 30_000);

  it('Globex TenantAdmin quick-login resolves to the dedicated database', async () => {
    const user = userEvent.setup();
    renderApp('/login');
    await user.click(screen.getByRole('button', { name: 'Quick login as admin@globex.com' }));

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/dashboard'), TIMEOUT);
    expect(await screen.findByText('db:nexusdesk_tenant_globex', undefined, TIMEOUT)).toBeInTheDocument();
  }, 30_000);

  it('rejects bad credentials with the backend ProblemDetails', async () => {
    const user = userEvent.setup();
    renderApp('/login');
    await user.click(screen.getByRole('radio', { name: 'acme' }));
    await user.type(screen.getByLabelText('Email'), 'admin@acme.com');
    await user.type(screen.getByLabelText('Password'), 'definitely-wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Invalid email or password.', undefined, TIMEOUT)).toBeInTheDocument();
  }, 30_000);
});
