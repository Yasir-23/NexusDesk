import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { TenantSummary } from '../../lib/types';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

function tenant(overrides: Partial<TenantSummary>): TenantSummary {
  return {
    id: 'id',
    name: 'Name',
    subdomain: 'sub',
    planTier: 'Pro',
    isolationStrategy: 'SharedDatabase',
    status: 'Active',
    maxAgents: 10,
    maxTicketsPerMonth: 5000,
    maxStorageBytes: 21474836480,
    createdAtUtc: '2026-11-10T00:00:00Z',
    ...overrides,
  };
}

function seed() {
  return [
    tenant({ id: 'acme-id', name: 'Acme Corporation', subdomain: 'acme' }),
    tenant({ id: 'beta-id', name: 'Beta Innovations', subdomain: 'beta', planTier: 'Free', maxAgents: 2, maxTicketsPerMonth: 100 }),
    tenant({
      id: 'globex-id',
      name: 'Globex Global',
      subdomain: 'globex',
      planTier: 'Enterprise',
      isolationStrategy: 'DedicatedDatabase',
      status: 'Suspended',
      maxAgents: 100,
      maxTicketsPerMonth: 100000,
    }),
  ];
}

/** Stateful backend: PATCH mutates the list so the re-fetch reflects the change. */
function mockPlatform() {
  const tenants = seed();
  return mockFetch({
    'GET /api/v1/platform/tenants': () => ({ body: { page: 1, pageSize: 100, totalCount: tenants.length, items: tenants } }),
    'PATCH /api/v1/platform/tenants/beta-id/status': (call) => {
      const { status } = call.body as { status: 'Active' | 'Suspended' };
      tenants[1] = { ...tenants[1], status };
      return { body: { id: 'beta-id', subdomain: 'beta', status, updatedAtUtc: '2026-11-16T18:35:00Z' } };
    },
    'PATCH /api/v1/platform/tenants/globex-id/status': (call) => {
      const { status } = call.body as { status: 'Active' | 'Suspended' };
      tenants[2] = { ...tenants[2], status };
      return { body: { id: 'globex-id', subdomain: 'globex', status, updatedAtUtc: '2026-11-16T18:35:00Z' } };
    },
  });
}

describe('SuperAdmin Tenant Directory', () => {
  it('renders every tenant with status, plan, isolation and quota badges', async () => {
    signInAs(USERS.superAdmin);
    const { find } = mockPlatform();
    renderApp('/platform/tenants');

    const acme = await screen.findByTestId('tenant-row-acme');
    expect(within(acme).getByText('Acme Corporation')).toBeInTheDocument();
    expect(within(acme).getByText('Active')).toBeInTheDocument();
    expect(within(acme).getByText('Pro')).toBeInTheDocument();
    expect(within(acme).getByText('Shared · RLS')).toBeInTheDocument();
    expect(within(acme).getByText(/10 agents · 5K tickets\/mo · 20 GB/)).toBeInTheDocument();

    const globex = screen.getByTestId('tenant-row-globex');
    expect(within(globex).getByText('Suspended')).toBeInTheDocument();
    expect(within(globex).getByText('Enterprise')).toBeInTheDocument();
    expect(within(globex).getByText('Dedicated DB')).toBeInTheDocument();

    expect(screen.getByText('Showing 3 of 3 tenants')).toBeInTheDocument();

    const [listCall] = find('GET', '/api/v1/platform/tenants');
    expect(listCall.url).toContain('page=1&pageSize=100');
    expect(listCall.headers.get('Authorization')).toBe('Bearer token-SuperAdmin');
    expect(listCall.headers.has('X-Tenant-Subdomain')).toBe(false);
  });

  it('filters by search text', async () => {
    const user = userEvent.setup();
    signInAs(USERS.superAdmin);
    mockPlatform();
    renderApp('/platform/tenants');
    await screen.findByTestId('tenant-row-acme');

    await user.type(screen.getByLabelText('Search tenants'), 'glob');

    expect(screen.queryByTestId('tenant-row-acme')).not.toBeInTheDocument();
    expect(screen.getByTestId('tenant-row-globex')).toBeInTheDocument();
    expect(screen.getByText('Showing 1 of 3 tenants')).toBeInTheDocument();
  });

  it('suspends a tenant with a required reason and refreshes the list', async () => {
    const user = userEvent.setup();
    signInAs(USERS.superAdmin);
    const { find } = mockPlatform();
    renderApp('/platform/tenants');
    await screen.findByTestId('tenant-row-beta');

    await user.click(screen.getByRole('button', { name: 'Suspend beta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Suspend tenant' });
    const confirm = within(dialog).getByRole('button', { name: 'Suspend tenant' });
    expect(confirm).toBeDisabled(); // reason is mandatory

    await user.type(within(dialog).getByLabelText('Reason'), 'Overdue invoice non-payment');
    await user.click(confirm);

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const [patch] = find('PATCH', '/api/v1/platform/tenants/beta-id/status');
    expect(patch.body).toEqual({ status: 'Suspended', reason: 'Overdue invoice non-payment' });
    expect(patch.headers.get('Content-Type')).toBe('application/json');

    // List was invalidated and re-fetched with the new status.
    await waitFor(() => expect(within(screen.getByTestId('tenant-row-beta')).getByText('Suspended')).toBeInTheDocument());
    expect(find('GET', '/api/v1/platform/tenants').length).toBeGreaterThanOrEqual(2);
    expect(await screen.findByText("Tenant 'beta' suspended")).toBeInTheDocument();
  });

  it('reactivates a suspended tenant', async () => {
    const user = userEvent.setup();
    signInAs(USERS.superAdmin);
    const { find } = mockPlatform();
    renderApp('/platform/tenants');
    await screen.findByTestId('tenant-row-globex');

    await user.click(screen.getByRole('button', { name: 'Activate globex' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reactivate tenant' });
    await user.click(within(dialog).getByRole('button', { name: 'Payment received' }));
    await user.click(within(dialog).getByRole('button', { name: 'Reactivate tenant' }));

    await waitFor(() => expect(within(screen.getByTestId('tenant-row-globex')).getByText('Active')).toBeInTheDocument());
    expect(find('PATCH', '/api/v1/platform/tenants/globex-id/status')[0].body).toEqual({ status: 'Active', reason: 'Payment received' });
  });

  it('renders backend ProblemDetails when the list fails', async () => {
    signInAs(USERS.superAdmin);
    mockFetch({
      'GET /api/v1/platform/tenants': { status: 403, body: { title: 'Forbidden', status: 403, detail: 'SuperAdmin role required.', code: 'FORBIDDEN' } },
    });
    renderApp('/platform/tenants');

    expect(await screen.findAllByText('SuperAdmin role required.')).not.toHaveLength(0);
    expect(screen.getAllByText(/FORBIDDEN · HTTP 403/).length).toBeGreaterThan(0);
  });
});
