import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { AuthUser, ChangePlanRequest } from '../../lib/types';
import { TENANT_USERS } from '../../test/fixtures';
import { PLANS, subscription } from '../../test/fixtures.m3';
import { mockFetch, renderApp, signInAs } from '../../test/utils';

const BETA_ADMIN: AuthUser = {
  id: '33333333-3333-3333-3333-333333333331',
  email: 'admin@beta.com',
  fullName: 'Beta Admin',
  role: 'TenantAdmin',
  tenantId: 'beta-tenant',
  subdomain: 'beta',
};

const QUOTA_PROBLEM = {
  type: 'https://nexusdesk.io/errors/plan-agent-limit-exceeded',
  title: 'Plan Agent Limit Exceeded',
  status: 422,
  detail: "Tenant 'beta' on plan 'Free' has reached the maximum quota of 2 active agents.",
  instance: '/api/v1/users/agents',
  code: 'PLAN_AGENT_LIMIT_EXCEEDED',
  errorCode: 'PLAN_AGENT_LIMIT_EXCEEDED',
};

function mockBilling(plan: 'Free' | 'Pro' = 'Free', agentReply: { status: number; body: unknown } = { status: 422, body: QUOTA_PROBLEM }) {
  let current = subscription(plan);
  return mockFetch({
    'GET /api/v1/billing/subscription': () => ({ body: current }),
    'GET /api/v1/billing/plans': { body: PLANS },
    'GET /api/v1/users': { body: TENANT_USERS },
    'POST /api/v1/users/agents': () => agentReply,
    'POST /api/v1/billing/subscription/change-plan': (call) => {
      current = subscription((call.body as ChangePlanRequest).planCode as 'Free' | 'Pro' | 'Enterprise');
      return { body: current };
    },
  });
}

describe('Billing & usage', () => {
  it('renders the current plan, billing cycle and usage progress bars', async () => {
    signInAs(BETA_ADMIN);
    mockBilling('Free');
    renderApp('/admin/billing');

    const plan = await screen.findByTestId('current-plan');
    expect(within(plan).getByText('Free Starter')).toBeInTheDocument();
    expect(screen.getByTestId('billing-cycle')).toHaveTextContent('Monthly');

    const agents = screen.getByRole('progressbar', { name: 'Active agents' });
    expect(agents).toHaveAttribute('aria-valuenow', '2');
    expect(agents).toHaveAttribute('aria-valuemax', '2');
    expect(within(screen.getByTestId('usage-active_agents')).getByText('Quota reached — upgrade to add more.')).toBeInTheDocument();

    const tickets = screen.getByRole('progressbar', { name: 'Tickets this period' });
    expect(tickets).toHaveAttribute('aria-valuenow', '42');
    expect(tickets).toHaveAttribute('aria-valuetext', '42 of 100 (42%)');
    expect((tickets.firstElementChild as HTMLElement).style.width).toBe('42%');
    expect(screen.getByRole('progressbar', { name: 'AI tokens this period' })).toHaveAttribute('aria-valuenow', '2840');
  });

  it('surfaces 422 PLAN_AGENT_LIMIT_EXCEEDED inline when adding an agent on the Free plan', async () => {
    const user = userEvent.setup();
    signInAs(BETA_ADMIN);
    const { find } = mockBilling('Free');
    renderApp('/admin/billing');

    await user.click((await screen.findAllByRole('button', { name: 'Add agent' }))[0]);
    const dialog = await screen.findByRole('dialog', { name: 'Add agent' });
    await user.type(within(dialog).getByLabelText('Full name'), 'Sarah Connor');
    await user.type(within(dialog).getByLabelText('Email'), 'sarah.agent@beta.com');
    await user.click(within(dialog).getByRole('button', { name: 'Add agent' }));

    const alert = await within(dialog).findByTestId('agent-quota-error');
    expect(alert).toHaveTextContent('Agent seat limit reached');
    expect(alert).toHaveTextContent("Tenant 'beta' on plan 'Free' has reached the maximum quota of 2 active agents.");
    expect(alert).toHaveTextContent('PLAN_AGENT_LIMIT_EXCEEDED · HTTP 422');

    const post = find('POST', '/api/v1/users/agents')[0];
    expect(post.headers.get('X-Tenant-Subdomain')).toBe('beta');
    expect(post.body).toEqual({ fullName: 'Sarah Connor', email: 'sarah.agent@beta.com', password: 'Password123!' });
    // Handled inline: the dialog stays open and no global error toast is raised.
    expect(screen.queryByText('Plan Agent Limit Exceeded')).not.toBeInTheDocument();
  });

  it('adds an agent when seats remain', async () => {
    const user = userEvent.setup();
    signInAs(BETA_ADMIN);
    mockBilling('Pro', {
      status: 201,
      body: { id: 'new', tenantId: 't', email: 'sarah.agent@beta.com', fullName: 'Sarah Connor', phoneNumber: null, role: 'Agent', isActive: true, createdAtUtc: '2026-11-16T18:40:00Z' },
    });
    renderApp('/admin/billing');
    await user.click((await screen.findAllByRole('button', { name: 'Add agent' }))[0]);
    const dialog = await screen.findByRole('dialog', { name: 'Add agent' });
    await user.type(within(dialog).getByLabelText('Full name'), 'Sarah Connor');
    await user.type(within(dialog).getByLabelText('Email'), 'sarah.agent@beta.com');
    await user.click(within(dialog).getByRole('button', { name: 'Add agent' }));
    expect(await screen.findByText('Agent added')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('upgrades the plan in one click via change-plan', async () => {
    const user = userEvent.setup();
    signInAs(BETA_ADMIN);
    const { find } = mockBilling('Free');
    renderApp('/admin/billing');

    const pro = await screen.findByTestId('plan-card-Pro');
    expect(within(screen.getByTestId('plan-card-Free')).getByRole('button', { name: 'Current plan' })).toBeDisabled();
    await user.click(within(pro).getByRole('button', { name: 'Upgrade to Pro' }));

    await waitFor(() => expect(find('POST', '/api/v1/billing/subscription/change-plan')).toHaveLength(1));
    expect(find('POST', '/api/v1/billing/subscription/change-plan')[0].body).toEqual({ planCode: 'Pro', billingCycle: 'Monthly' });
    await waitFor(() => expect(within(screen.getByTestId('current-plan')).getByText('Professional Team')).toBeInTheDocument());
    expect(within(screen.getByTestId('plan-card-Pro')).getByRole('button', { name: 'Current plan' })).toBeDisabled();
    expect(screen.getByRole('progressbar', { name: 'Active agents' })).toHaveAttribute('aria-valuemax', '10');
  });

  it('handles the live catalog shape: extra tiers ordered by price and unlimited meters', async () => {
    signInAs(BETA_ADMIN);
    const starter = { ...PLANS[1], id: 'plan-Starter', code: 'Starter', name: 'Starter Plan', monthlyPriceCents: 1900, maxAgents: 5 };
    const sub = subscription('Pro');
    sub.usage.push({ metricName: 'webhook_deliveries', used: 7, limit: -1, remaining: -1, usagePercentage: 0, remainingPercentage: 100 });
    mockFetch({
      'GET /api/v1/billing/subscription': { body: sub },
      'GET /api/v1/billing/plans': { body: [...PLANS, starter] },
      'GET /api/v1/users': { body: TENANT_USERS },
    });
    renderApp('/admin/billing');

    await screen.findByTestId('plan-card-Starter');
    const order = screen.getAllByTestId(/^plan-card-/).map((c) => c.getAttribute('data-testid'));
    expect(order).toEqual(['plan-card-Free', 'plan-card-Starter', 'plan-card-Pro', 'plan-card-Enterprise']);
    expect(within(screen.getByTestId('plan-card-Starter')).getByRole('button', { name: 'Switch to Starter' })).toBeEnabled();
    expect(within(screen.getByTestId('plan-card-Enterprise')).getByRole('button', { name: 'Upgrade to Enterprise' })).toBeEnabled();
    const webhooks = screen.getByTestId('usage-webhook_deliveries');
    expect(webhooks).toHaveTextContent('Webhook deliveries this period7 · Unlimited');
    expect(within(webhooks).queryByRole('progressbar')).not.toBeInTheDocument();
  });
});
