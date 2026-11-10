# Database, Event Store & Vector Schema Specification
**Project:** NexusDesk  
**Version:** 1.0.0  
**Database Engine:** PostgreSQL 16+ with `pgvector` and `uuid-ossp` extensions  

---

## 1. Master Catalog Schema (`catalog` schema — Global Plane)

These tables do NOT have tenant RLS because they govern global tenant routing and plans.

### 1.1 `catalog.tenants`
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | PK | Unique Tenant ID |
| `name` | `varchar(120)` | NOT NULL | Company Display Name |
| `subdomain` | `varchar(63)` | UNIQUE, NOT NULL | Lowercase DNS slug (`acme`) |
| `plan_tier` | `varchar(20)` | NOT NULL | `Free`, `Pro`, `Enterprise` |
| `isolation_mode` | `varchar(20)` | NOT NULL | `SharedRls` or `DedicatedDb` |
| `encrypted_connection_string` | `text` | NULL | Populated via `IDataProtector` when `DedicatedDb` |
| `status` | `varchar(20)` | NOT NULL | `Active`, `Suspended`, `Decommissioned` |
| `max_agents` | `int` | NOT NULL | Quota limit from plan |
| `max_tickets_per_month` | `int` | NOT NULL | Quota limit from plan |
| `created_at_utc` | `timestamptz` | NOT NULL | Creation timestamp |

### 1.2 `catalog.tenant_usage_meters`
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | PK | Meter Record ID |
| `tenant_id` | `uuid` | FK (`tenants.id`), INDEX | Target Tenant |
| `billing_period` | `char(7)` | NOT NULL | Format `YYYY-MM` |
| `active_agents_count` | `int` | NOT NULL DEFAULT 0 | Current agent seats used |
| `tickets_created_count` | `int` | NOT NULL DEFAULT 0 | Monthly ticket counter |
| `storage_bytes_used` | `bigint` | NOT NULL DEFAULT 0 | Attachment storage used |
| `ai_tokens_consumed` | `bigint` | NOT NULL DEFAULT 0 | Total RAG prompt/completion tokens |

---

## 2. Tenant-Scoped Relational Schema (`app` schema — Enforced via RLS)

Every table in the `app` schema MUST include `tenant_id uuid NOT NULL` and apply the following PostgreSQL Row-Level Security policy:

```sql
ALTER TABLE app.<table_name> ENABLE ROW LEVEL SECURITY;
ALTER TABLE app.<table_name> FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_policy ON app.<table_name>
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
```

### 2.1 `app.users`
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | PK | User ID |
| `tenant_id` | `uuid` | NOT NULL, INDEX | Owning Tenant ID |
| `email` | `varchar(256)` | NOT NULL | Unique per `(tenant_id, email)` |
| `password_hash` | `text` | NOT NULL | ASP.NET Identity PBKDF2 / Argon2 hash |
| `full_name` | `varchar(150)` | NOT NULL | Redacted on GDPR request |
| `role` | `varchar(30)` | NOT NULL | `SuperAdmin`, `TenantAdmin`, `Agent`, `Customer` |
| `is_active` | `boolean` | NOT NULL DEFAULT true | Deactivated or suspended flag |
| `is_gdpr_anonymized` | `boolean` | NOT NULL DEFAULT false | True after GDPR erasure |
| `created_at_utc` | `timestamptz` | NOT NULL | Audit creation timestamp |

### 2.2 `app.sla_policies`
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | PK | SLA Policy ID |
| `tenant_id` | `uuid` | NOT NULL, INDEX | Owning Tenant ID |
| `priority` | `varchar(20)` | NOT NULL | `Low`, `Medium`, `High`, `Urgent` |
| `first_response_target_minutes` | `int` | NOT NULL | Max minutes until first agent reply |
| `resolution_target_minutes` | `int` | NOT NULL | Max minutes until ticket resolved |

### 2.3 `app.ticket_read_models` (CQRS Read Projection built from Marten Events)
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | PK | Ticket Stream ID |
| `tenant_id` | `uuid` | NOT NULL, INDEX | Owning Tenant ID |
| `ticket_number` | `varchar(30)` | NOT NULL | Human-readable e.g. `ND-10042` |
| `title` | `varchar(250)` | NOT NULL | Ticket Title (supports Expand/Contract rename from `subject`) |
| `description` | `text` | NOT NULL | Initial problem description |
| `status` | `varchar(30)` | NOT NULL | `New`, `Assigned`, `InProgress`, `PendingCustomer`, `Resolved`, `Closed` |
| `priority` | `varchar(20)` | NOT NULL | `Low`, `Medium`, `High`, `Urgent` |
| `category` | `varchar(80)` | NOT NULL | AI or manual category |
| `customer_id` | `uuid` | NOT NULL | Creator Customer ID |
| `assigned_agent_id` | `uuid` | NULL | Assigned Agent ID |
| `tags` | `text[]` | NOT NULL | Array of string tags |
| `first_response_due_at_utc` | `timestamptz` | NOT NULL | Calculated from SLA Policy |
| `resolution_due_at_utc` | `timestamptz` | NOT NULL | Calculated from SLA Policy |
| `is_sla_breached` | `boolean` | NOT NULL DEFAULT false | Set true on `TicketSlaBreachedEvent` |
| `version` | `int` | NOT NULL | Event stream version number |
| `created_at_utc` | `timestamptz` | NOT NULL | Ticket creation time |
| `updated_at_utc` | `timestamptz` | NOT NULL | Last event timestamp |

### 2.4 `app.kb_articles` & `app.kb_embeddings` (`pgvector`)
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | PK | Article / Chunk ID |
| `tenant_id` | `uuid` | NOT NULL, INDEX | Owning Tenant ID (RLS enforced) |
| `article_id` | `uuid` | FK | Parent Article ID |
| `title` | `varchar(200)` | NOT NULL | Article Title |
| `category` | `varchar(80)` | NOT NULL | KB Category |
| `slug` | `varchar(200)` | NOT NULL | URL-friendly slug |
| `content_markdown` | `text` | NOT NULL | Full or chunked Markdown content |
| `version` | `int` | NOT NULL DEFAULT 1 | Incremented on every edit |
| `is_published` | `boolean` | NOT NULL DEFAULT false | Visibility flag |
| `embedding` | `vector(1536)` | NOT NULL | HNSW indexed vector embedding |

### 2.5 `app.automation_rules` & `app.webhook_subscriptions` & `app.outbox_messages`
* `app.automation_rules`: `id`, `tenant_id`, `name`, `trigger_event`, `condition_json`, `action_type`, `action_payload_json`, `is_enabled`.
* `app.webhook_subscriptions`: `id`, `tenant_id`, `target_url`, `encrypted_secret`, `event_types[]`, `is_active`.
* `app.outbox_messages`: `id`, `tenant_id`, `event_type`, `payload_json`, `retry_count`, `next_retry_at_utc`, `status` (`Pending`, `Processed`, `DeadLettered`), `last_error`.

---

## 3. Immutable Ticket Event Stream Definitions (Marten Event Store)

All events implement `ITenantScopedEvent { Guid TenantId { get; } }`:

1. `TicketCreatedEvent(Guid TicketId, Guid TenantId, string TicketNumber, string Title, string Description, string Priority, string Category, Guid CustomerId, DateTimeOffset CreatedAtUtc, DateTimeOffset FirstResponseDueAtUtc, DateTimeOffset ResolutionDueAtUtc)`
2. `TicketAssignedEvent(Guid TicketId, Guid TenantId, Guid AgentId, Guid AssignedByUserId, DateTimeOffset AssignedAtUtc)`
3. `TicketPriorityChangedEvent(Guid TicketId, Guid TenantId, string OldPriority, string NewPriority, string Reason, DateTimeOffset ChangedAtUtc)`
4. `TicketMessageAddedEvent(Guid TicketId, Guid TenantId, Guid MessageId, Guid AuthorUserId, string AuthorRole, string Body, bool IsInternalNote, DateTimeOffset SentAtUtc)`
5. `TicketAttachmentAddedEvent(Guid TicketId, Guid TenantId, Guid AttachmentId, string FileName, string ContentType, long SizeBytes, string BlobUri, string VirusScanStatus, DateTimeOffset UploadedAtUtc)`
6. `TicketSlaBreachedEvent(Guid TicketId, Guid TenantId, string BreachType, DateTimeOffset BreachedAtUtc)`
7. `TicketResolvedEvent(Guid TicketId, Guid TenantId, Guid ResolvedByAgentId, string ResolutionSummary, DateTimeOffset ResolvedAtUtc)`
8. `TicketCustomerAnonymizedEvent(Guid TicketId, Guid TenantId, Guid CustomerId, DateTimeOffset AnonymizedAtUtc)`