/**
 * Milestone 3 live checks against the running backend (through the Vite dev proxy).
 * Run with `npm run test:live` while NexusDesk.Api (:56100) and Vite (:5173) are up.
 *
 * Responses are checked key-by-key against the contract types. Side effects are limited to:
 *   - re-PUTting an existing SLA policy with its *current* values (idempotent upsert),
 *   - two requests the backend must reject (no rows created): an agent on the full `beta`
 *     Free plan (422) and the contract's example automation trigger (400). The agent probe
 *     only runs when beta's seats are confirmed full, so it can never create an agent.
 * Notifications, plans, GDPR erasure and portal submissions are never mutated here.
 */
import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ApiProblemError, api, apiDownload } from '../lib/api';
import { login } from '../lib/queries';
import type {
  AgentPerformance,
  AnalyticsOverview,
  AutomationRule,
  DailyTicketTrend,
  DeadLetterWebhook,
  OutboxMessage,
  PagedAuditLogsResponse,
  PagedNotificationsResponse,
  PagedTicketsResponse,
  SlaPolicy,
  SubscriptionPlan,
  TenantSubscriptionDetails,
  TicketDetails,
  WebhookSubscription,
} from '../lib/types';
import { renderApp } from './utils';

const TIMEOUT = { timeout: 20_000 };
const PASSWORD = 'Password123!';

function expectKeys(obj: object, keys: string[], label: string) {
  const missing = keys.filter((k) => !(k in obj));
  expect(missing, `${label} is missing contract keys`).toEqual([]);
}

async function rejection(p: Promise<unknown>): Promise<ApiProblemError> {
  const e = await p.then(() => null, (err: unknown) => err);
  expect(e).toBeInstanceOf(ApiProblemError);
  return e as ApiProblemError;
}

describe('M3 contract: analytics (admin@acme.com)', () => {
  it('overview, trends, agent performance and CSV export', async () => {
    await login('admin@acme.com', PASSWORD, 'acme');

    const overview = await api.get<AnalyticsOverview>('/api/v1/analytics/overview', { query: { days: 30 }, silent: true });
    expectKeys(overview, ['totalTickets', 'openTickets', 'resolvedTickets', 'slaComplianceRate', 'averageFirstResponseMinutes', 'averageResolutionMinutes', 'csatAverageScore', 'activeChatSessions', 'publishedKbArticles'], 'AnalyticsOverview');
    for (const v of Object.values(overview)) expect(typeof v).toBe('number');

    const trends = await api.get<DailyTicketTrend[]>('/api/v1/analytics/tickets/trends', { query: { days: 7 }, silent: true });
    expect(Array.isArray(trends)).toBe(true);
    for (const t of trends) {
      expectKeys(t, ['date', 'createdCount', 'resolvedCount', 'breachedCount'], 'DailyTicketTrend');
      expect(t.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    }

    const agents = await api.get<AgentPerformance[]>('/api/v1/analytics/agents/performance', { query: { days: 30 }, silent: true });
    for (const a of agents) expectKeys(a, ['agentUserId', 'agentName', 'assignedTickets', 'resolvedTickets', 'averageResolutionMinutes', 'slaComplianceRate'], 'AgentPerformance');

    const { blob, fileName } = await apiDownload('/api/v1/analytics/export/tickets.csv?days=30');
    expect(fileName).toMatch(/\.csv$/);
    expect((await blob.text()).split(/\r?\n/)[0]).toBe('TicketNumber,Title,Status,Priority,Category,CustomerEmail,AssignedAgentEmail,IsSlaBreached,CreatedAtUtc,ResolvedAtUtc');
  }, 30_000);
});

describe('M3 contract: tenant admin (admin@acme.com)', () => {
  it('SLA policies list + idempotent upsert', async () => {
    await login('admin@acme.com', PASSWORD, 'acme');
    const policies = await api.get<SlaPolicy[]>('/api/v1/tenant-admin/sla-policies', { silent: true });
    expect(policies.length).toBeGreaterThan(0);
    for (const p of policies) {
      expectKeys(p, ['id', 'tenantId', 'priority', 'firstResponseTargetMinutes', 'resolutionTargetMinutes', 'createdAtUtc', 'updatedAtUtc'], 'SlaPolicyResponse');
      expect(['Low', 'Medium', 'High', 'Urgent']).toContain(p.priority);
    }
    const p = policies[0];
    const saved = await api.put<SlaPolicy>(
      '/api/v1/tenant-admin/sla-policies',
      { priority: p.priority, firstResponseTargetMinutes: p.firstResponseTargetMinutes, resolutionTargetMinutes: p.resolutionTargetMinutes },
      { silent: true },
    );
    expect(saved.id).toBe(p.id);
    expect(saved.firstResponseTargetMinutes).toBe(p.firstResponseTargetMinutes);
  }, 30_000);

  it('automation rules, webhooks, outbox and dead-letter queue shapes', async () => {
    await login('admin@acme.com', PASSWORD, 'acme');

    const rules = await api.get<AutomationRule[]>('/api/v1/tenant-admin/automation-rules', { silent: true });
    for (const r of rules) expectKeys(r, ['id', 'tenantId', 'name', 'triggerEvent', 'conditionJson', 'actionType', 'actionPayloadJson', 'isEnabled', 'createdAtUtc', 'updatedAtUtc'], 'AutomationRuleResponse');

    // Contract drift guard: the doc's example values are rejected; the UI only offers the accepted ones.
    const drift = await rejection(
      api.post('/api/v1/tenant-admin/automation-rules', { name: 'contract example', triggerEvent: 'ticket.created', conditionJson: '{}', actionType: 'assign_to_tier2', actionPayloadJson: '{}', isEnabled: false }, { silent: true }),
    );
    expect(drift.status).toBe(400);
    expect(drift.code).toBe('AutomationRule.UnsupportedTrigger');

    const hooks = await api.get<WebhookSubscription[]>('/api/v1/tenant-admin/webhooks', { silent: true });
    for (const w of hooks) expectKeys(w, ['id', 'tenantId', 'targetUrl', 'eventTypes', 'isActive', 'createdAtUtc', 'updatedAtUtc'], 'WebhookSubscriptionResponse');

    const outbox = await api.get<OutboxMessage[]>('/api/v1/tenant-admin/webhooks/outbox', { silent: true });
    for (const m of outbox) {
      expectKeys(m, ['id', 'tenantId', 'eventType', 'payloadJson', 'status', 'retryCount', 'nextRetryAtUtc', 'lastError', 'createdAtUtc', 'processedAtUtc'], 'OutboxMessageResponse');
      expect(['Pending', 'Processed', 'DeadLettered', 'Dispatched', 'Failed']).toContain(m.status);
    }

    const dlq = await api.get<DeadLetterWebhook[]>('/api/v1/tenant-admin/webhooks/dead-letters', { silent: true });
    for (const d of dlq) expectKeys(d, ['id', 'outboxMessageId', 'webhookSubscriptionId', 'targetUrl', 'eventType', 'payloadJson', 'attemptCount', 'lastHttpStatusCode', 'lastError', 'failedAtUtc'], 'DeadLetterWebhookResponse');

    const audit = await api.get<PagedAuditLogsResponse>('/api/v1/audit-logs', { query: { actorEmail: 'admin@acme.com', page: 1, pageSize: 50 }, silent: true });
    expectKeys(audit, ['items', 'page', 'pageSize', 'totalCount', 'totalPages'], 'PagedAuditLogsResponse');
    expect(audit.pageSize).toBe(50);
    for (const e of audit.items) expectKeys(e, ['id', 'tenantId', 'actorUserId', 'actorEmail', 'action', 'resourceType', 'resourceId', 'metadataJson', 'ipAddress', 'createdAtUtc'], 'AuditLogEntryResponse');
  }, 30_000);
});

describe('M3 contract: billing & quotas', () => {
  it('plans catalog and subscription usage meters', async () => {
    await login('admin@acme.com', PASSWORD, 'acme');
    const plans = await api.get<SubscriptionPlan[]>('/api/v1/billing/plans', { silent: true });
    // The contract's three tiers must exist; the live catalog may hold more (v1.0.0 adds `Starter`).
    expect(plans.map((p) => p.code)).toEqual(expect.arrayContaining(['Free', 'Pro', 'Enterprise']));
    for (const p of plans) expectKeys(p, ['id', 'code', 'name', 'monthlyPriceCents', 'maxAgents', 'maxMonthlyTickets', 'maxMonthlyAiTokens', 'customBrandingEnabled', 'webhooksEnabled', 'slaEnabled', 'isActive'], 'SubscriptionPlanDto');

    const sub = await api.get<TenantSubscriptionDetails>('/api/v1/billing/subscription', { silent: true });
    expectKeys(sub, ['subscription', 'plan', 'periodKey', 'usage'], 'TenantSubscriptionDetailsDto');
    expectKeys(sub.subscription, ['id', 'tenantId', 'planCode', 'status', 'billingCycle', 'currentPeriodStartUtc', 'currentPeriodEndUtc', 'cancelAtPeriodEnd', 'updatedAtUtc'], 'TenantSubscriptionDto');
    expect(sub.usage.map((u) => u.metricName)).toEqual(expect.arrayContaining(['active_agents', 'ai_tokens_used', 'tickets_created']));
    for (const u of sub.usage) expectKeys(u, ['metricName', 'used', 'limit', 'remaining', 'usagePercentage', 'remainingPercentage'], 'MetricUsageDto');
  }, 30_000);

  it('beta (Free, 2 agents) rejects a third agent with 422 PLAN_AGENT_LIMIT_EXCEEDED', async (ctx) => {
    await login('admin@beta.com', PASSWORD, 'beta');
    const sub = await api.get<TenantSubscriptionDetails>('/api/v1/billing/subscription', { silent: true });
    const seats = sub.usage.find((u) => u.metricName === 'active_agents')!;
    expect(sub.plan.code).toBe('Free');
    // Never risk creating a real agent: only probe when the quota is already exhausted.
    if (seats.used < seats.limit) ctx.skip();

    const err = await rejection(
      api.post('/api/v1/users/agents', { email: `quota.probe.${Date.now()}@beta.com`, fullName: 'Quota Probe', password: PASSWORD }, { silent: true }),
    );
    expect(err.status).toBe(422);
    expect(err.code).toBe('PLAN_AGENT_LIMIT_EXCEEDED');
  }, 30_000);
});

describe('M3 contract: customer portal & notifications (customer@acme.com)', () => {
  it('portal list/detail are scoped and scrubbed; notifications page shape', async () => {
    const me = await login('customer@acme.com', PASSWORD, 'acme');
    const list = await api.get<PagedTicketsResponse>('/api/v1/portal/tickets', { query: { page: 1, pageSize: 20 }, silent: true });
    expectKeys(list, ['items', 'page', 'pageSize', 'totalCount', 'totalPages'], 'PagedTicketsResponse');
    expect(list.items.every((t) => t.customerId === me.user.id)).toBe(true);

    if (list.items.length > 0) {
      const t = await api.get<TicketDetails>(`/api/v1/portal/tickets/${list.items[0].id}`, { silent: true });
      expect(t.comments.every((c) => c.type !== 'InternalNote')).toBe(true);
      expect(t.activityLogs ?? []).toEqual([]);
    }

    const notifications = await api.get<PagedNotificationsResponse>('/api/v1/notifications', { query: { unreadOnly: false, page: 1, pageSize: 10 }, silent: true });
    expectKeys(notifications, ['items', 'unreadCount', 'page', 'pageSize', 'totalCount', 'totalPages'], 'PagedNotificationsResponse');
    for (const n of notifications.items) expectKeys(n, ['id', 'tenantId', 'recipientUserId', 'type', 'title', 'message', 'linkUrl', 'isRead', 'readAtUtc', 'createdAtUtc'], 'UserNotificationResponse');
  }, 30_000);
});

describe('M3 UI against the live backend', () => {
  it('admin sees billing meters, the plan catalog and webhook inspectors', async () => {
    await login('admin@acme.com', PASSWORD, 'acme');
    renderApp('/admin/billing');
    expect(await screen.findByRole('progressbar', { name: 'Active agents' }, TIMEOUT)).toBeInTheDocument();
    expect(await screen.findByTestId('plan-card-Enterprise', undefined, TIMEOUT)).toBeInTheDocument();
    expect(within(screen.getByTestId('plan-card-Pro')).getByRole('button', { name: 'Current plan' })).toBeDisabled();

    await waitFor(() => expect(screen.getByRole('link', { name: 'Webhooks & DLQ' })).toBeInTheDocument(), TIMEOUT);
    screen.getByRole('link', { name: 'Webhooks & DLQ' }).click();
    expect(await screen.findByTestId('outbox-inspector', undefined, TIMEOUT)).toBeInTheDocument();
    expect(await screen.findByTestId('dlq-inspector', undefined, TIMEOUT)).toBeInTheDocument();
  }, 60_000);

  it('agent sees analytics KPIs and the trend panel', async () => {
    await login('agent@acme.com', PASSWORD, 'acme');
    renderApp('/analytics');
    await waitFor(() => expect(screen.getByTestId('kpi-totalTickets')).toHaveTextContent(/\d/), TIMEOUT);
    expect(await screen.findByText(/Daily ticket trends/, undefined, TIMEOUT)).toBeInTheDocument();
  }, 60_000);

  it('customer lands on My Requests with the notifications bell', async () => {
    await login('customer@acme.com', PASSWORD, 'acme');
    renderApp('/portal');
    expect(await screen.findByRole('heading', { name: 'My Requests' }, TIMEOUT)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /^Notifications/ }, TIMEOUT)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Submit New Request' }, TIMEOUT)).toBeInTheDocument();
  }, 60_000);
});
