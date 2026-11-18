import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { AutomationRule, CreateAutomationRuleRequest, PagedAuditLogsResponse, UpsertSlaPolicyRequest } from '../../lib/types';
import { AGENT_ID, TENANT_USERS } from '../../test/fixtures';
import { DEAD_LETTERS, OUTBOX, SLA_POLICIES, auditEntry, automationRule, slaPolicy } from '../../test/fixtures.m3';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

describe('SLA policies', () => {
  it('lists policies per priority and upserts with PUT, updating the row live', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    let policies = [...SLA_POLICIES];
    const { find } = mockFetch({
      'GET /api/v1/tenant-admin/sla-policies': () => ({ body: policies }),
      'PUT /api/v1/tenant-admin/sla-policies': (call) => {
        const b = call.body as UpsertSlaPolicyRequest;
        const saved = slaPolicy({ id: `sla-${b.priority}`, ...b, updatedAtUtc: new Date().toISOString() });
        policies = [...policies.filter((p) => p.priority !== b.priority), saved];
        return { body: saved };
      },
    });
    renderApp('/admin/sla');

    const urgent = await screen.findByTestId('sla-row-Urgent');
    expect(within(urgent).getByText('(15 min)')).toBeInTheDocument();
    expect(within(urgent).getByText('(120 min)')).toBeInTheDocument();
    expect(within(screen.getByTestId('sla-row-Low')).getByText('Not configured')).toBeInTheDocument();

    // Urgent is selected by default and pre-filled from the existing policy.
    const editor = screen.getByRole('form', { name: 'SLA policy editor' });
    const first = within(editor).getByLabelText('First response target (minutes)');
    expect(first).toHaveValue(15);
    await user.clear(first);
    await user.type(first, '10');
    await user.click(within(editor).getByRole('button', { name: 'Save Urgent policy' }));

    await waitFor(() => expect(find('PUT', '/api/v1/tenant-admin/sla-policies')).toHaveLength(1));
    const put = find('PUT', '/api/v1/tenant-admin/sla-policies')[0];
    expect(put.body).toEqual({ priority: 'Urgent', firstResponseTargetMinutes: 10, resolutionTargetMinutes: 120 });
    expect(put.headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect(put.headers.get('Authorization')).toBe('Bearer token-TenantAdmin');

    await waitFor(() => expect(within(screen.getByTestId('sla-row-Urgent')).getByText('(10 min)')).toBeInTheDocument());
    expect(within(screen.getByTestId('sla-row-Urgent')).getByText('Saved')).toBeInTheDocument();

    // Creating a policy for an unconfigured priority.
    await user.click(screen.getByTestId('sla-row-Low'));
    await user.click(screen.getByRole('button', { name: 'Save Low policy' }));
    await waitFor(() => expect(find('PUT', '/api/v1/tenant-admin/sla-policies')).toHaveLength(2));
    expect(find('PUT', '/api/v1/tenant-admin/sla-policies')[1].body).toEqual({
      priority: 'Low',
      firstResponseTargetMinutes: 480,
      resolutionTargetMinutes: 4320,
    });
    await waitFor(() => expect(within(screen.getByTestId('sla-row-Low')).queryByText('Not configured')).not.toBeInTheDocument());
  });

  it('blocks a resolution target shorter than the first-response target', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    mockFetch({ 'GET /api/v1/tenant-admin/sla-policies': { body: SLA_POLICIES } });
    renderApp('/admin/sla');
    const editor = await screen.findByRole('form', { name: 'SLA policy editor' });
    await waitFor(() => expect(within(editor).getByLabelText('Resolution target (minutes)')).toHaveValue(120));
    await user.clear(within(editor).getByLabelText('Resolution target (minutes)'));
    await user.type(within(editor).getByLabelText('Resolution target (minutes)'), '5');
    expect(within(editor).getByText('Resolution target must be at least the first-response target.')).toBeInTheDocument();
    expect(within(editor).getByRole('button', { name: 'Save Urgent policy' })).toBeDisabled();
  });
});

describe('Automation rules', () => {
  function mockRules(initial: AutomationRule[]) {
    let rules = [...initial];
    return mockFetch({
      'GET /api/v1/tenant-admin/automation-rules': () => ({ body: rules }),
      'GET /api/v1/users': { body: TENANT_USERS },
      'POST /api/v1/tenant-admin/automation-rules': (call) => {
        const b = call.body as CreateAutomationRuleRequest;
        const created = automationRule({ id: 'rule-new', ...b, createdAtUtc: new Date().toISOString() });
        rules = [created, ...rules];
        return { status: 201, body: created };
      },
      'PATCH /api/v1/tenant-admin/automation-rules/rule-1/toggle': (call) => {
        const { isEnabled } = call.body as { isEnabled: boolean };
        rules = rules.map((r) => (r.id === 'rule-1' ? { ...r, isEnabled } : r));
        return { body: rules.find((r) => r.id === 'rule-1') };
      },
    });
  }

  it('creates a rule from a one-click preset template', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    const { find } = mockRules([]);
    renderApp('/admin/automation');

    await user.click(await screen.findByRole('button', { name: 'Create first rule' }));
    const dialog = await screen.findByRole('dialog', { name: 'New automation rule' });
    await user.click(within(dialog).getByRole('button', { name: /Auto-escalate High tickets to Urgent/ }));
    expect(within(dialog).getByLabelText('Rule name')).toHaveValue('Auto-escalate High tickets to Urgent');
    await user.click(within(dialog).getByRole('button', { name: 'Create rule' }));

    await waitFor(() => expect(find('POST', '/api/v1/tenant-admin/automation-rules')).toHaveLength(1));
    expect(find('POST', '/api/v1/tenant-admin/automation-rules')[0].body).toEqual({
      name: 'Auto-escalate High tickets to Urgent',
      triggerEvent: 'TicketCreated',
      conditionJson: '{"priority":"High"}',
      actionType: 'EscalatePriority',
      actionPayloadJson: '{"priority":"Urgent"}',
      isEnabled: true,
    });
    const row = await screen.findByTestId('rule-row-rule-new');
    expect(within(row).getByText('TicketCreated')).toBeInTheDocument();
    expect(within(row).getByRole('switch')).toHaveAttribute('aria-checked', 'true');
  });

  it('fills the AssignAgent preset with a real agent id and validates JSON', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    mockRules([]);
    renderApp('/admin/automation');
    await user.click(await screen.findByRole('button', { name: 'New rule' }));
    const dialog = await screen.findByRole('dialog', { name: 'New automation rule' });
    // Wait for the tenant user list that supplies the agent id.
    await waitFor(async () => {
      await user.click(within(dialog).getByRole('button', { name: /Route Urgent tickets/ }));
      expect(within(dialog).getByLabelText('Action payload JSON')).toHaveValue(`{"agentId":"${AGENT_ID}"}`);
    });
    const condition = within(dialog).getByLabelText('Condition JSON');
    await user.clear(condition);
    await user.type(condition, 'not json');
    expect(within(dialog).getByText('Invalid JSON')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Create rule' })).toBeDisabled();
  });

  it('toggles a rule with PATCH {isEnabled} and reflects it instantly', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    const { find } = mockRules([automationRule()]);
    renderApp('/admin/automation');

    const toggle = await screen.findByRole('switch', { name: 'Enable Escalate on SLA breach' });
    expect(toggle).toHaveAttribute('aria-checked', 'true');
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-checked', 'false'); // optimistic
    await waitFor(() => expect(find('PATCH', '/automation-rules/rule-1/toggle')).toHaveLength(1));
    expect(find('PATCH', '/automation-rules/rule-1/toggle')[0].body).toEqual({ isEnabled: false });
    expect(await screen.findByText('Rule disabled')).toBeInTheDocument();
  });
});

describe('Webhooks, outbox and dead-letter queue', () => {
  function mockWebhooks() {
    return mockFetch({
      'GET /api/v1/tenant-admin/webhooks': { body: [] },
      'GET /api/v1/tenant-admin/webhooks/outbox': { body: OUTBOX },
      'GET /api/v1/tenant-admin/webhooks/dead-letters': { body: DEAD_LETTERS },
      'POST /api/v1/tenant-admin/webhooks': (call) => ({
        status: 201,
        body: { id: 'wh-new', tenantId: 't', isActive: true, createdAtUtc: new Date().toISOString(), updatedAtUtc: null, ...(call.body as object) },
      }),
    });
  }

  it('renders outbox statuses (backend Processed/DeadLettered mapped) and filters by status', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    mockWebhooks();
    renderApp('/admin/webhooks');

    const outbox = await screen.findByTestId('outbox-inspector');
    expect(await within(outbox).findByTestId('outbox-row-ob-1')).toHaveTextContent('Dispatched');
    expect(within(outbox).getByTestId('outbox-row-ob-2')).toHaveTextContent('Pending');
    expect(within(outbox).getByTestId('outbox-row-ob-2')).toHaveTextContent('Receiver returned 503');
    expect(within(outbox).getByTestId('outbox-row-ob-3')).toHaveTextContent('Failed');
    expect(within(outbox).getByTestId('outbox-row-ob-3')).toHaveTextContent('DeadLettered');

    await user.click(within(outbox).getByRole('button', { name: /^Failed 1/ }));
    expect(within(outbox).queryByTestId('outbox-row-ob-1')).not.toBeInTheDocument();
    expect(within(outbox).getByTestId('outbox-row-ob-3')).toBeInTheDocument();

    await user.click(within(outbox).getByRole('button', { name: 'Show payload for ticket.sla_breached' }));
    expect(within(outbox).getByText(/"ticketNumber": "ND-10003"/)).toBeInTheDocument();
  });

  it('shows dead letters with attempt count, last HTTP status and last error', async () => {
    signInAs(USERS.acmeAdmin);
    mockWebhooks();
    renderApp('/admin/webhooks');
    const dlq = await screen.findByTestId('dlq-inspector');
    const row = await within(dlq).findByTestId('dlq-row-dl-1');
    expect(within(row).getByText('5')).toBeInTheDocument();
    expect(within(row).getByText('HTTP 503')).toBeInTheDocument();
    expect(within(row).getByText('Service Unavailable')).toBeInTheDocument();
    expect(within(dlq).getByText('1 dead-lettered')).toBeInTheDocument();
  });

  it('registers a webhook with the selected event types', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    const { find } = mockWebhooks();
    renderApp('/admin/webhooks');

    const form = await screen.findByRole('form', { name: 'Register webhook' });
    await user.type(within(form).getByLabelText('Signing secret'), 'whsec_super_secret_signing_key_12345');
    await user.click(within(form).getByLabelText('ticket.resolved'));
    await user.click(within(form).getByRole('button', { name: 'Register webhook' }));

    await waitFor(() => expect(find('POST', '/api/v1/tenant-admin/webhooks')).toHaveLength(1));
    expect(find('POST', '/api/v1/tenant-admin/webhooks')[0].body).toEqual({
      targetUrl: 'https://webhook.site/test-receiver',
      secret: 'whsec_super_secret_signing_key_12345',
      eventTypes: ['ticket.created', 'ticket.sla_breached'],
      isActive: true,
    });
  });
});

describe('Audit log explorer', () => {
  it('queries with actorEmail + pageSize 50, pages, and expands metadataJson', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    const page = (n: number): PagedAuditLogsResponse => ({
      items:
        n === 1
          ? [auditEntry(), auditEntry({ id: 'audit-2', actorEmail: 'agent@acme.com', action: 'ticket.created', resourceType: 'ticket' })]
          : [auditEntry({ id: 'audit-3', action: 'privacy.forget_me', resourceType: 'user' })],
      page: n,
      pageSize: 50,
      totalCount: 3,
      totalPages: 2,
    });
    const { find } = mockFetch({
      'GET /api/v1/audit-logs': (call) => ({ body: page(Number(new URL(call.url, 'http://l').searchParams.get('page'))) }),
    });
    renderApp('/admin/audit');

    expect(await screen.findByTestId('audit-row-audit-2')).toBeInTheDocument();
    expect(find('GET', '/api/v1/audit-logs')[0].url).toContain('page=1&pageSize=50');

    await user.type(screen.getByLabelText('Filter by actor email'), 'admin@acme.com');
    await waitFor(() => expect(find('GET', '/api/v1/audit-logs').at(-1)!.url).toContain('actorEmail=admin%40acme.com'));
    await waitFor(() => expect(screen.queryByTestId('audit-row-audit-2')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Show metadata for sla.upsert' }));
    expect(screen.getByText(/"firstResponse": 15/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Next/ }));
    expect(await screen.findByTestId('audit-row-audit-3')).toBeInTheDocument();
    expect(find('GET', '/api/v1/audit-logs').at(-1)!.url).toContain('page=2');
  });

  it('is not reachable for agents', async () => {
    signInAs(USERS.acmeAgent);
    mockFetch({});
    renderApp('/admin/audit');
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/dashboard'));
  });
});
