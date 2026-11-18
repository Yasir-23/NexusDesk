// Milestone 3 fixtures: tenant admin, billing, analytics, portal and notifications.
import type {
  AgentPerformance,
  AnalyticsOverview,
  AuditLogEntry,
  AutomationRule,
  DailyTicketTrend,
  DeadLetterWebhook,
  OutboxMessage,
  PagedNotificationsResponse,
  SlaPolicy,
  SubscriptionPlan,
  TenantPlanTier,
  TenantSubscriptionDetails,
  UserNotification,
} from '../lib/types';
import { ADMIN_ID, AGENT_ID, CUSTOMER_ID, TENANT_ID } from './fixtures';

export function slaPolicy(overrides: Partial<SlaPolicy> = {}): SlaPolicy {
  return {
    id: 'sla-urgent',
    tenantId: TENANT_ID,
    priority: 'Urgent',
    firstResponseTargetMinutes: 15,
    resolutionTargetMinutes: 120,
    createdAtUtc: '2026-11-10T00:00:00Z',
    updatedAtUtc: '2026-11-16T19:25:00Z',
    ...overrides,
  };
}

export const SLA_POLICIES: SlaPolicy[] = [
  slaPolicy({ id: 'sla-high', priority: 'High', firstResponseTargetMinutes: 60, resolutionTargetMinutes: 480 }),
  slaPolicy(),
];

export function automationRule(overrides: Partial<AutomationRule> = {}): AutomationRule {
  return {
    id: 'rule-1',
    tenantId: TENANT_ID,
    name: 'Escalate on SLA breach',
    triggerEvent: 'TicketSlaBreached',
    conditionJson: '{}',
    actionType: 'EscalatePriority',
    actionPayloadJson: '{}',
    isEnabled: true,
    createdAtUtc: '2026-11-15T10:00:00Z',
    updatedAtUtc: null,
    ...overrides,
  };
}

export const OUTBOX: OutboxMessage[] = [
  {
    id: 'ob-1',
    tenantId: TENANT_ID,
    eventType: 'ticket.created',
    payloadJson: '{"ticketNumber":"ND-10001"}',
    status: 'Processed',
    retryCount: 0,
    nextRetryAtUtc: '2026-11-16T18:45:00Z',
    lastError: null,
    createdAtUtc: '2026-11-16T18:45:00Z',
    processedAtUtc: '2026-11-16T18:45:02Z',
  },
  {
    id: 'ob-2',
    tenantId: TENANT_ID,
    eventType: 'ticket.resolved',
    payloadJson: '{"ticketNumber":"ND-10002"}',
    status: 'Pending',
    retryCount: 2,
    nextRetryAtUtc: '2026-11-16T19:05:00Z',
    lastError: 'Receiver returned 503',
    createdAtUtc: '2026-11-16T18:55:00Z',
    processedAtUtc: null,
  },
  {
    id: 'ob-3',
    tenantId: TENANT_ID,
    eventType: 'ticket.sla_breached',
    payloadJson: '{"ticketNumber":"ND-10003"}',
    status: 'DeadLettered',
    retryCount: 5,
    nextRetryAtUtc: '2026-11-16T19:45:00Z',
    lastError: 'Connection refused',
    createdAtUtc: '2026-11-16T18:30:00Z',
    processedAtUtc: '2026-11-16T19:45:00Z',
  },
];

export const DEAD_LETTERS: DeadLetterWebhook[] = [
  {
    id: 'dl-1',
    tenantId: TENANT_ID,
    outboxMessageId: 'ob-3',
    webhookSubscriptionId: 'wh-1',
    targetUrl: 'https://webhook.site/broken-receiver',
    eventType: 'ticket.sla_breached',
    payloadJson: '{"ticketNumber":"ND-10003","priority":"Urgent"}',
    attemptCount: 5,
    lastHttpStatusCode: 503,
    lastError: 'Service Unavailable',
    failedAtUtc: '2026-11-16T19:45:00Z',
  },
];

function plan(code: TenantPlanTier, name: string, cents: number, agents: number, tickets: number, tokens: number, paid: boolean): SubscriptionPlan {
  return {
    id: `plan-${code}`,
    code,
    name,
    monthlyPriceCents: cents,
    maxAgents: agents,
    maxMonthlyTickets: tickets,
    maxMonthlyAiTokens: tokens,
    customBrandingEnabled: paid,
    webhooksEnabled: paid,
    slaEnabled: paid,
    isActive: true,
  };
}

export const PLANS: SubscriptionPlan[] = [
  plan('Free', 'Free Starter', 0, 2, 100, 10_000, false),
  plan('Pro', 'Professional Team', 4900, 10, 5000, 500_000, true),
  plan('Enterprise', 'Enterprise Dedicated', 19900, 100, 100_000, 5_000_000, true),
];

function meter(metricName: string, used: number, limit: number) {
  const usagePercentage = (used / limit) * 100;
  return { metricName, used, limit, remaining: limit - used, usagePercentage, remainingPercentage: 100 - usagePercentage };
}

/** Free = the `beta` tenant with both agent seats used. */
export function subscription(planCode: TenantPlanTier = 'Free'): TenantSubscriptionDetails {
  const p = PLANS.find((x) => x.code === planCode)!;
  return {
    subscription: {
      id: 'sub-1',
      tenantId: TENANT_ID,
      planCode,
      status: 'Active',
      billingCycle: 'Monthly',
      stripeCustomerId: null,
      stripeSubscriptionId: null,
      currentPeriodStartUtc: '2026-11-01T00:00:00Z',
      currentPeriodEndUtc: '2026-12-01T00:00:00Z',
      cancelAtPeriodEnd: false,
      updatedAtUtc: '2026-11-01T00:00:00Z',
    },
    plan: p,
    periodKey: '2026-11',
    usage: [
      meter('active_agents', planCode === 'Free' ? 2 : 3, p.maxAgents),
      meter('tickets_created', 42, p.maxMonthlyTickets),
      meter('ai_tokens_used', 2840, p.maxMonthlyAiTokens),
    ],
  };
}

export const OVERVIEW: AnalyticsOverview = {
  totalTickets: 248,
  openTickets: 18,
  resolvedTickets: 230,
  slaComplianceRate: 98.4,
  averageFirstResponseMinutes: 14.2,
  averageResolutionMinutes: 96.5,
  csatAverageScore: 4.85,
  activeChatSessions: 3,
  publishedKbArticles: 28,
};

export const TRENDS: DailyTicketTrend[] = [
  { date: '2026-11-10', createdCount: 35, resolvedCount: 32, breachedCount: 1 },
  { date: '2026-11-11', createdCount: 40, resolvedCount: 38, breachedCount: 0 },
  { date: '2026-11-12', createdCount: 28, resolvedCount: 30, breachedCount: 2 },
];

export const AGENT_PERFORMANCE: AgentPerformance[] = [
  { agentUserId: ADMIN_ID, agentName: 'Acme Admin', assignedTickets: 20, resolvedTickets: 15, averageResolutionMinutes: 130, slaComplianceRate: 75 },
  { agentUserId: AGENT_ID, agentName: 'Acme Agent', assignedTickets: 115, resolvedTickets: 112, averageResolutionMinutes: 84.1, slaComplianceRate: 99.1 },
];

export function auditEntry(overrides: Partial<AuditLogEntry> = {}): AuditLogEntry {
  return {
    id: 'audit-1',
    tenantId: TENANT_ID,
    actorUserId: ADMIN_ID,
    actorEmail: 'admin@acme.com',
    action: 'sla.upsert',
    resourceType: 'sla_policy',
    resourceId: 'sla-urgent',
    metadataJson: '{"priority":"Urgent","firstResponse":15}',
    ipAddress: '127.0.0.1',
    createdAtUtc: '2026-11-16T19:25:00Z',
    ...overrides,
  };
}

export function notification(overrides: Partial<UserNotification> = {}): UserNotification {
  return {
    id: 'n-1',
    tenantId: TENANT_ID,
    recipientUserId: CUSTOMER_ID,
    type: 'ticket.reply',
    title: 'New reply on ND-10001',
    message: 'Support replied to your request.',
    linkUrl: '/tickets/652f5d1c-aed3-426d-aad5-48273c5140ad',
    isRead: false,
    readAtUtc: null,
    createdAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

export function notificationsPage(items: UserNotification[]): PagedNotificationsResponse {
  return { items, unreadCount: items.filter((n) => !n.isRead).length, page: 1, pageSize: 10, totalCount: items.length, totalPages: 1 };
}
