// Contract types mirrored 1:1 from docs/frontend_api_contract.md.
// Property names are camelCase and enum values are the exact backend strings.

export type Role = 'SuperAdmin' | 'TenantAdmin' | 'Agent' | 'Customer';
export type TenantPlanTier = 'Free' | 'Pro' | 'Enterprise';
export type IsolationStrategy = 'SharedDatabase' | 'DedicatedDatabase';
export type TenantStatus = 'Active' | 'Suspended';

/** RFC 7807 ProblemDetails (§1.4), plus ASP.NET validation `errors`. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  errorCode?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

// ---------- Module 1: Auth ----------

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthUser {
  id: string;
  email: string;
  fullName: string;
  role: Role;
  tenantId: string | null;
  subdomain: string | null;
  phoneNumber?: string | null;
  ssoProvider?: string | null;
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  refreshToken: string;
  user: AuthUser;
}

/** `POST /connect/token` response (snake_case per OAuth2 spec). */
export interface OAuthTokenResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
  refresh_token?: string;
  scope?: string;
}

export interface MeResponse {
  id: string;
  email: string;
  fullName: string;
  phoneNumber: string | null;
  role: Role;
  tenantId: string | null;
  isActive: boolean;
  permissions: string[];
  ssoProvider: string | null;
  lastLoginAtUtc: string | null;
  createdAtUtc: string;
}

export interface RegisterCustomerRequest {
  email: string;
  fullName: string;
  password: string;
  phoneNumber?: string;
}

export interface RegisterCustomerResponse {
  id: string;
  tenantId: string;
  email: string;
  fullName: string;
  role: Role;
  createdAtUtc: string;
}

// ---------- Module 2: Platform & Tenant Administration ----------

export interface ProvisionTenantRequest {
  organizationName: string;
  subdomain: string;
  adminEmail: string;
  adminFullName: string;
  planTier: TenantPlanTier;
  adminPassword: string;
}

export interface PlanLimits {
  maxAgents: number;
  maxTicketsPerMonth: number;
  maxStorageBytes: number;
}

export interface ProvisionTenantResponse {
  tenantId: string;
  organizationName: string;
  subdomain: string;
  portalUrl: string;
  status: TenantStatus;
  isolationStrategy: IsolationStrategy;
  planTier: TenantPlanTier;
  planLimits: PlanLimits;
  createdAtUtc: string;
}

export interface TenantSummary {
  id: string;
  name: string;
  subdomain: string;
  planTier: TenantPlanTier;
  isolationStrategy: IsolationStrategy;
  status: TenantStatus;
  maxAgents: number;
  maxTicketsPerMonth: number;
  maxStorageBytes: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface PagedTenantsResponse {
  page: number;
  pageSize: number;
  totalCount: number;
  items: TenantSummary[];
}

export interface TenantAuditLog {
  id: string;
  tenantId: string;
  action: string;
  reason: string | null;
  performedBy: string;
  timestampUtc: string;
}

export interface TenantDetailsResponse {
  tenant: TenantSummary;
  auditLogs: TenantAuditLog[];
}

export interface UpdateTenantStatusRequest {
  status: TenantStatus;
  reason: string;
}

export interface UpdateTenantStatusResponse {
  id: string;
  subdomain: string;
  status: TenantStatus;
  updatedAtUtc: string;
}

export interface TenantContext {
  tenantId: string;
  subdomain: string;
  planTier: TenantPlanTier;
  isolationStrategy: IsolationStrategy;
  status: TenantStatus;
  isResolved: boolean;
}

export interface TenantSetting {
  id: string;
  key: string;
  value: string;
  tenantId: string;
}

export interface TenantMembership {
  id: string;
  userId: string;
  role: Role;
  email: string;
  fullName: string;
  tenantId: string;
}

export interface TenantCurrentResponse {
  tenantContext: TenantContext;
  activeDatabase: string;
  rlsSessionTenantId: string | null;
  settingsCount: number;
  settings: TenantSetting[];
  membershipsCount: number;
  memberships: TenantMembership[];
}

// ---------- Module 9: System Diagnostics ----------

export interface SystemInfoResponse {
  application: string;
  environment: string;
  framework: string;
  osVersion: string;
  processArchitecture: string;
  loadedModules: string[];
  connectivity: {
    overall: string;
    postgresql: string;
    redis: string;
  };
  timestampUtc: string;
}

// ---------- Module 3: Agent Ticketing & File Attachments ----------

export type TicketStatus = 'New' | 'Open' | 'PendingCustomer' | 'PendingInternal' | 'Resolved' | 'Closed';
export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Urgent';
export type TicketChannel = 'WebPortal' | 'Email' | 'Api' | 'LiveChat';
export type CommentType = 'PublicReply' | 'InternalNote';

export const TICKET_STATUSES: TicketStatus[] = ['New', 'Open', 'PendingCustomer', 'PendingInternal', 'Resolved', 'Closed'];
export const TICKET_PRIORITIES: TicketPriority[] = ['Low', 'Medium', 'High', 'Urgent'];

/** `GET /api/v1/users` row. */
export interface TenantUser {
  id: string;
  tenantId: string;
  email: string;
  fullName: string;
  phoneNumber: string | null;
  role: Role;
  isActive: boolean;
  isAnonymized: boolean;
  lastLoginAtUtc: string | null;
  createdAtUtc: string;
}

export interface CreateTicketRequest {
  title: string;
  description: string;
  priority: TicketPriority;
  channel: TicketChannel;
  customerId: string;
  tags: string[];
}

export interface TicketSummary {
  id: string;
  tenantId: string;
  ticketNumber: string;
  title: string;
  status: TicketStatus;
  priority: TicketPriority;
  channel: TicketChannel;
  customerId: string;
  assignedAgentId: string | null;
  firstResponseDueUtc: string | null;
  resolutionDueUtc: string | null;
  firstRespondedAtUtc: string | null;
  resolvedAtUtc: string | null;
  closedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  rowVersion: number;
  tags: string[];
  isSlaBreached: boolean;
  slaBreachedAtUtc: string | null;
}

export interface TicketComment {
  id: string;
  authorUserId: string;
  isCustomer: boolean;
  type: CommentType;
  body: string;
  createdAtUtc: string;
}

export interface TicketActivityLog {
  id: string;
  performedByUserId: string;
  action: string;
  oldValue: string | null;
  newValue: string | null;
  timestampUtc: string;
}

export interface TicketDetails extends TicketSummary {
  description: string;
  resolutionNotes: string | null;
  comments: TicketComment[];
  activityLogs: TicketActivityLog[];
}

export interface PagedTicketsResponse {
  items: TicketSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface TicketListQuery {
  status?: TicketStatus;
  priority?: TicketPriority;
  assignedAgentId?: string;
  customerId?: string;
  page: number;
  pageSize: number;
}

export interface AssignTicketRequest {
  agentId: string;
  rowVersion: number;
}

export interface UpdateTicketStatusRequest {
  status: TicketStatus;
  resolutionNotes?: string;
  reason?: string;
  rowVersion: number;
}

export interface UpdateTicketPriorityRequest {
  priority: TicketPriority;
  rowVersion: number;
}

export interface AddCommentRequest {
  body: string;
  type: CommentType;
}

export interface TicketAttachment {
  id: string;
  ticketId: string;
  uploadedByUserId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
}

// ---------- Module 5: Omnichannel Live Chat ----------

export type ChatSessionStatus = 'Waiting' | 'Active' | 'Closed' | 'ConvertedToTicket';

export interface ChatMessage {
  id: string;
  tenantId: string;
  chatSessionId: string;
  senderUserId: string;
  senderRole: string;
  content: string;
  sentAtUtc: string;
}

/** `ChatSessionSummaryDto` (list rows). */
export interface ChatSessionSummary {
  id: string;
  tenantId: string;
  customerId: string;
  assignedAgentId: string | null;
  subject: string;
  status: ChatSessionStatus;
  convertedTicketId: string | null;
  createdAtUtc: string;
  closedAtUtc: string | null;
  messageCount?: number;
}

/** `ChatSessionDto` (detail, create, claim). */
export interface ChatSession extends Omit<ChatSessionSummary, 'messageCount'> {
  messages?: ChatMessage[] | null;
}

export interface ChatSessionClosed {
  id: string;
  tenantId: string;
  status: ChatSessionStatus;
  convertedTicketId: string | null;
  closedAtUtc: string;
}

// ---------- Module 7: Knowledge Base & AI Assist ----------

export type KbArticleStatus = 'Draft' | 'Published' | 'Archived';

export interface KbCategory {
  id: string;
  tenantId: string;
  name: string;
  slug: string;
  description: string | null;
  displayOrder: number;
  createdAtUtc: string;
  publishedArticlesCount: number;
}

export interface KbArticle {
  id: string;
  tenantId: string;
  categoryId: string;
  categoryName: string | null;
  title: string;
  slug: string;
  summary: string | null;
  bodyMarkdown: string;
  tags: string[];
  status: KbArticleStatus;
  authorUserId: string;
  viewCount: number;
  helpfulCount: number;
  notHelpfulCount: number;
  publishedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PagedKbArticlesResponse {
  items: KbArticle[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CreateKbArticleRequest {
  categoryId: string;
  title: string;
  slug: string;
  summary: string;
  bodyMarkdown: string;
  tags: string[];
  publishImmediately: boolean;
}

export interface KbSearchResult {
  id: string;
  title: string;
  slug: string;
  summary: string | null;
  categoryName: string;
  relevanceScore: number;
  helpfulCount: number;
}

export interface AiInstructionsRequest {
  customInstructions?: string;
}

export interface ReferencedArticle {
  id: string;
  title: string;
  slug: string;
}

export interface SuggestReplyResponse {
  ticketId: string;
  suggestedReply: string;
  referencedArticles: ReferencedArticle[];
}

export interface SummarizeTicketResponse {
  ticketId: string;
  summary: string;
  keyPoints: string[];
  commentCount: number;
}

// ---------- SignalR payloads (contract §4) ----------

/** `TicketCreated` / `TicketUpdated`. */
export interface TicketRealtimeEvent {
  ticketId: string;
  tenantId: string;
  ticketNumber: string;
  title: string;
  status: TicketStatus;
  priority: TicketPriority;
  customerId: string;
  assignedAgentId: string | null;
  eventType: string;
  occurredAtUtc: string;
}

export interface TicketCommentAddedEvent {
  ticketId: string;
  tenantId: string;
  commentId: string;
  authorUserId: string;
  isCustomer: boolean;
  type: CommentType;
  body: string;
  createdAtUtc: string;
}

export interface PresenceAgent {
  userId: string;
  displayName: string;
  isTyping: boolean;
  lastActivityUtc: string;
}

export interface AgentPresenceUpdatedEvent {
  ticketId: string;
  tenantId: string;
  activeAgents: PresenceAgent[];
  updatedAtUtc: string;
}

export interface SlaBreachAlertEvent {
  ticketId: string;
  ticketNumber: string;
  priority: TicketPriority;
  breachType: string;
  breachedAtUtc: string;
}

export interface ChatTypingIndicatorEvent {
  sessionId: string;
  userId: string;
  userName: string | null;
  isTyping: boolean;
}

// ---------- Module 1: Privacy ----------

/** `POST /api/v1/privacy/forget-me`. */
export interface ForgetMeResponse {
  anonymized: boolean;
  userId: string;
  anonymizedAtUtc: string;
}

// ---------- Module 2: Agents ----------

/** `POST /api/v1/users/agents` (422 `PLAN_AGENT_LIMIT_EXCEEDED` when the plan quota is used up). */
export interface CreateAgentRequest {
  email: string;
  fullName: string;
  password: string;
  phoneNumber?: string;
}

export interface CreateAgentResponse {
  id: string;
  tenantId: string;
  email: string;
  fullName: string;
  phoneNumber: string | null;
  role: Role;
  isActive: boolean;
  createdAtUtc: string;
}

// ---------- Module 4: Customer Portal ----------

export interface PortalCreateTicketRequest {
  title: string;
  description: string;
  priority: TicketPriority;
  tags: string[];
}

/** `POST /api/v1/portal/tickets/{id}/replies`. */
export interface PortalReplyRequest {
  message: string;
}

export interface PortalTicketListQuery {
  status?: TicketStatus;
  page: number;
  pageSize: number;
}

// ---------- Module 6: SLA, Automation, Webhooks/Outbox/DLQ ----------

export interface UpsertSlaPolicyRequest {
  priority: TicketPriority;
  firstResponseTargetMinutes: number;
  resolutionTargetMinutes: number;
}

export interface SlaPolicy extends UpsertSlaPolicyRequest {
  id: string;
  tenantId: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

/**
 * Values the backend validator accepts (`AutomationRule.SupportedTriggerEvents` /
 * `SupportedActionTypes`). The contract's `ticket.created` / `assign_to_tier2` example is
 * rejected with 400 `AutomationRule.UnsupportedTrigger`, so the UI only offers these.
 */
export type AutomationTriggerEvent = 'TicketCreated' | 'TicketSlaBreached' | 'TicketPriorityChanged';
export type AutomationActionType = 'EscalatePriority' | 'AssignAgent' | 'AddInternalNote';
export const AUTOMATION_TRIGGERS: AutomationTriggerEvent[] = ['TicketCreated', 'TicketSlaBreached', 'TicketPriorityChanged'];
export const AUTOMATION_ACTIONS: AutomationActionType[] = ['EscalatePriority', 'AssignAgent', 'AddInternalNote'];

export interface CreateAutomationRuleRequest {
  name: string;
  triggerEvent: string;
  conditionJson: string;
  actionType: string;
  actionPayloadJson: string;
  isEnabled: boolean;
}

export interface AutomationRule extends CreateAutomationRuleRequest {
  id: string;
  tenantId: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface ToggleAutomationRuleRequest {
  isEnabled: boolean;
}

export type WebhookEventType = 'ticket.created' | 'ticket.sla_breached' | 'ticket.resolved';
export const WEBHOOK_EVENT_TYPES: WebhookEventType[] = ['ticket.created', 'ticket.sla_breached', 'ticket.resolved'];

export interface CreateWebhookRequest {
  targetUrl: string;
  secret: string;
  eventTypes: string[];
  isActive: boolean;
}

export interface WebhookSubscription {
  id: string;
  tenantId: string;
  targetUrl: string;
  eventTypes: string[];
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

/**
 * Outbox status. The contract lists `Pending` / `Dispatched` / `Failed`; the backend enum
 * (`OutboxMessageStatus`) actually serializes `Pending` / `Processed` / `DeadLettered`.
 * Both spellings are handled by the UI.
 */
export type OutboxStatus = 'Pending' | 'Processed' | 'DeadLettered' | 'Dispatched' | 'Failed';

export interface OutboxMessage {
  id: string;
  tenantId: string;
  eventType: string;
  payloadJson: string;
  status: OutboxStatus;
  retryCount: number;
  nextRetryAtUtc: string;
  lastError: string | null;
  createdAtUtc: string;
  processedAtUtc: string | null;
}

export interface DeadLetterWebhook {
  id: string;
  tenantId: string;
  outboxMessageId: string;
  webhookSubscriptionId: string;
  targetUrl: string;
  eventType: string;
  payloadJson: string;
  attemptCount: number;
  lastHttpStatusCode: number | null;
  lastError: string;
  failedAtUtc: string;
}

// ---------- Module 8: Billing ----------

/**
 * Billing plan code. The contract lists Free / Pro / Enterprise, but the live catalog can hold
 * more (v1.0.0 also seeds `Starter`), so unknown codes are allowed.
 */
export type PlanCode = TenantPlanTier | (string & {});

export interface SubscriptionPlan {
  id: string;
  code: PlanCode;
  name: string;
  monthlyPriceCents: number;
  maxAgents: number;
  maxMonthlyTickets: number;
  maxMonthlyAiTokens: number;
  customBrandingEnabled: boolean;
  webhooksEnabled: boolean;
  slaEnabled: boolean;
  isActive: boolean;
}

export type BillingCycle = 'Monthly' | 'Annual';

export interface TenantSubscription {
  id: string;
  tenantId: string;
  planCode: PlanCode;
  status: string;
  billingCycle: string;
  stripeCustomerId: string | null;
  stripeSubscriptionId: string | null;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
  cancelAtPeriodEnd: boolean;
  updatedAtUtc: string;
}

export type UsageMetricName = 'active_agents' | 'tickets_created' | 'ai_tokens_used';

export interface MetricUsage {
  metricName: UsageMetricName | string;
  used: number;
  /** `-1` means unlimited (e.g. `webhook_deliveries`). */
  limit: number;
  remaining: number;
  usagePercentage: number;
  remainingPercentage: number;
}

export interface TenantSubscriptionDetails {
  subscription: TenantSubscription;
  plan: SubscriptionPlan;
  periodKey: string;
  usage: MetricUsage[];
}

export interface ChangePlanRequest {
  planCode: PlanCode;
  billingCycle: string;
}

// ---------- Module 9: Analytics, Audit, Notifications ----------

export interface AnalyticsOverview {
  totalTickets: number;
  openTickets: number;
  resolvedTickets: number;
  slaComplianceRate: number;
  averageFirstResponseMinutes: number;
  averageResolutionMinutes: number;
  csatAverageScore: number;
  activeChatSessions: number;
  publishedKbArticles: number;
}

export interface DailyTicketTrend {
  date: string;
  createdCount: number;
  resolvedCount: number;
  breachedCount: number;
}

export interface AgentPerformance {
  agentUserId: string;
  agentName: string;
  assignedTickets: number;
  resolvedTickets: number;
  averageResolutionMinutes: number;
  slaComplianceRate: number;
}

export interface AuditLogEntry {
  id: string;
  tenantId: string;
  actorUserId: string | null;
  actorEmail: string | null;
  action: string;
  resourceType: string;
  resourceId: string;
  metadataJson: string | null;
  ipAddress: string | null;
  createdAtUtc: string;
}

export interface PagedAuditLogsResponse {
  items: AuditLogEntry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AuditLogQuery {
  actorEmail?: string;
  resourceType?: string;
  page: number;
  pageSize: number;
}

export interface UserNotification {
  id: string;
  tenantId: string;
  recipientUserId: string;
  type: string;
  title: string;
  message: string;
  linkUrl: string | null;
  isRead: boolean;
  readAtUtc: string | null;
  createdAtUtc: string;
}

export interface PagedNotificationsResponse {
  items: UserNotification[];
  unreadCount: number;
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface MarkAllNotificationsReadResponse {
  markedCount: number;
}
