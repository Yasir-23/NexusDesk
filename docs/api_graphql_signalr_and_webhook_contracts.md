# API, GraphQL, SignalR & Webhook Contracts
**Project:** NexusDesk  
**Version:** 1.0.0  

---

## 1. Required HTTP Headers & Tenant Resolution

All API calls to tenant-scoped endpoints MUST provide:
* `Authorization: Bearer <JWT>`
* `X-Tenant-Subdomain: <subdomain>` (or resolve via `Host: <subdomain>.nexusdesk.local`)

Standard Error Response (`application/problem+json` — RFC 7807):
```json
{
  "type": "https://nexusdesk.io/errors/quota-exceeded",
  "title": "Plan Quota Exceeded",
  "status": 429,
  "detail": "Tenant 'acme' on plan 'Free' has reached the maximum limit of 2 active agents.",
  "instance": "/api/v1/agents",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

---

## 2. REST API Endpoints (`/api/v1`)

### 2.1 Authentication & Identity
* `POST /connect/token` — OpenIddict OAuth2 Token Endpoint (`authorization_code` + PKCE, `password` for dev/test seeding, `client_credentials`).
* `GET /api/v1/auth/me` — Returns current user profile, `TenantId`, `Subdomain`, `Role`, and active `FeatureFlags`.
* `POST /api/v1/privacy/forget-me` — Executes GDPR anonymization for the authenticated customer.

### 2.2 Platform Super Admin (`/api/v1/platform`)
* `POST /api/v1/platform/tenants` — Onboard new tenant (`Name`, `Subdomain`, `PlanTier`, `AdminEmail`, `AdminPassword`). Returns `201 Created` with live subdomain metadata.
* `GET /api/v1/platform/tenants` — List all tenants with current usage metering and health status.
* `PATCH /api/v1/platform/tenants/{tenantId}/status` — Suspend or reactivate a tenant (`{ "status": "Suspended", "reason": "Non-payment" }`).
* `POST /api/v1/platform/projections/rebuild/{ticketId}` — Deletes the read projection for `ticketId` and replays its Marten event stream from event 0.

### 2.3 Tenant Administration (`/api/v1/tenant-admin`)
* `GET /api/v1/tenant-admin/agents` — List tenant agents and seat usage vs plan quota.
* `POST /api/v1/tenant-admin/agents` — Create agent (Enforces `MaxAgents` quota; returns `422`/`402` Upgrade Prompt if limit reached).
* `PUT /api/v1/tenant-admin/sla-policies` — Configure SLA response/resolution targets.
* `POST /api/v1/tenant-admin/automation-rules` — Create automated IF/THEN escalation rule.
* `POST /api/v1/tenant-admin/webhooks` — Register external webhook receiver URL and secret.
* `GET /api/v1/tenant-admin/billing/invoices/{yyyy-mm}/pdf` — Generates and streams the monthly usage invoice PDF.

### 2.4 Ticketing & Attachments (`/api/v1/tickets`)
* `POST /api/v1/tickets` — Create ticket (Triggers AI auto-categorization, SLA timer calculation, and `TicketCreatedEvent`).
* `GET /api/v1/tickets` — High-speed source-generated JSON list of tickets for the current tenant.
* `GET /api/v1/tickets/{ticketId}` — Returns `TicketReadModel` plus latest messages.
* `GET /api/v1/tickets/{ticketId}/events` — Returns the immutable chronological event history stream (`TicketCreated`, `TicketAssigned`, etc.).
* `POST /api/v1/tickets/{ticketId}/assign` — Assigns ticket to an agent.
* `POST /api/v1/tickets/{ticketId}/messages` — Appends a customer reply or internal agent note.
* `POST /api/v1/tickets/{ticketId}/resolve` — Marks ticket resolved and stops SLA clock.
* `POST /api/v1/tickets/{ticketId}/attachments` — Multipart upload with magic-byte check and simulated virus scan.

### 2.5 Knowledge Base & Tenant-Scoped AI (`/api/v1/kb` & `/api/v1/ai`)
* `POST /api/v1/kb/articles` — Create/update KB article; if `isPublished == true`, chunks text and generates `pgvector` embeddings.
* `GET /api/v1/kb/search?q={query}&semantic={true|false}` — Full-text + vector semantic search strictly scoped to `TenantId`.
* `POST /api/v1/ai/tickets/{ticketId}/suggest-reply` — Runs `PromptInjectionGuard`, retrieves top-3 tenant KB chunks via `pgvector`, and returns a grounded draft response with cited `articleIds`.
* `POST /api/v1/ai/tickets/{ticketId}/summarize` — Generates a structured executive summary of the ticket event stream.

---

## 3. GraphQL Schema (`/graphql` — HotChocolate)

Agents can query rich relational shapes in a single round-trip (strictly filtered by `TenantContext`):

```graphql
type Query {
  tickets(
    where: TicketFilterInput
    order: [TicketSortInput!]
    first: Int
    after: String
  ): TicketsConnection!
  
  analyticsSummary: TenantAnalyticsDto!
}

type TicketReadModel {
  id: UUID!
  ticketNumber: String!
  title: String!
  status: String!
  priority: String!
  category: String!
  isSlaBreached: Boolean!
  firstResponseDueAtUtc: DateTime!
  resolutionDueAtUtc: DateTime!
  assignedAgentId: UUID
  customerId: UUID!
  tags: [String!]!
  createdAtUtc: DateTime!
}
```

---

## 4. SignalR Real-Time Hub Contract (`/hubs/chat`)

* **Connection:** Requires JWT query/header authentication; user is automatically joined to group `tenant:{TenantId}` and `ticket:{TicketId}`.
* **Client -> Server Methods:**
  * `JoinTicketRoom(Guid ticketId)`
  * `SendChatMessage(Guid ticketId, string message)`
  * `SendTypingIndicator(Guid ticketId, bool isTyping)`
* **Server -> Client Events:**
  * `ReceiveChatMessage(ChatMessageDto payload)`
  * `UserTypingChanged(Guid ticketId, Guid userId, string userName, bool isTyping)`
  * `AgentPresenceUpdated(Guid userId, bool isOnline)`
  * `SlaBreachAlert(Guid ticketId, string ticketNumber, string priority)`

---

## 5. Outbound Webhook Payload & Signature Specification

* **Headers:**
  * `Content-Type: application/json`
  * `X-NexusDesk-Event: ticket.created | ticket.sla_breached | ticket.resolved`
  * `X-NexusDesk-Delivery: <Guid>`
  * `X-NexusDesk-Timestamp: <UnixEpochSeconds>`
  * `X-NexusDesk-Signature-256: sha256=<HexEncodedHMAC>`

```json
{
  "deliveryId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "tenantId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "eventType": "ticket.sla_breached",
  "occurredAtUtc": "2026-11-09T18:30:00Z",
  "data": {
    "ticketId": "550e8400-e29b-41d4-a716-446655440000",
    "ticketNumber": "ND-10042",
    "priority": "Urgent",
    "breachType": "FirstResponseTime"
  }
}
```