# Software Design Document (SDD)
**Project:** NexusDesk  
**Version:** 1.0.0  

---

## 1. High-Level Solution Topology

NexusDesk is structured as a **Modular Monolith with an Extracted Native-AOT Micro-Service**, orchestrated locally via **.NET Aspire** and deployed to **Azure Container Apps / AKS**.

```text
NexusDesk/
├── docs/                                         # Architecture & Engineering Specifications
├── src/
│   ├── NexusDesk.AppHost/                        # .NET Aspire Orchestrator (Postgres, Redis, API, Web)
│   ├── NexusDesk.ServiceDefaults/                # OpenTelemetry, HealthChecks, Resilience Policies
│   ├── NexusDesk.Analyzers/                      # Custom Roslyn Analyzer (ND0001 Tenant Filter Guard)
│   ├── NexusDesk.SharedKernel/                   # TenantContext, Domain Primitives, Result<T>, Encryption
│   ├── Modules/
│   │   ├── NexusDesk.Modules.Identity/           # OpenIddict Server, OAuth2/PKCE, RBAC, GDPR Anonymizer
│   │   ├── NexusDesk.Modules.Tenants/            # Onboarding, Plan Quotas, Hybrid DB Connection Resolver
│   │   ├── NexusDesk.Modules.Ticketing/          # Marten Event Store, CQRS Projections, SLA Timers
│   │   ├── NexusDesk.Modules.Communications/     # SignalR Chat Hub (Redis Backplane), Outbox Webhooks
│   │   ├── NexusDesk.Modules.KnowledgeAi/        # KB CRUD, pgvector RAG, Semantic Kernel, Injection Guard
│   │   └── NexusDesk.Modules.Analytics/          # gRPC Live Stream, Usage Metering, PDF Invoice Generator
│   ├── NexusDesk.Api/                            # Main Host: REST Controllers, HotChocolate GraphQL, RateLimiter
│   ├── NexusDesk.WebhookSigner.Aot/              # Standalone Native AOT High-Throughput Webhook Worker
│   └── nexusdesk-web/                            # React 19 + TypeScript + Vite + Tailwind CSS Frontend
├── tests/
│   ├── NexusDesk.UnitTests/                      # Domain, FsCheck Property Tests, Stryker.NET Mutation Target
│   ├── NexusDesk.IntegrationTests/               # Testcontainers (Postgres + Redis), 10 Hard Challenges
│   ├── NexusDesk.Benchmarks/                     # BenchmarkDotNet Hot-Path Optimization Suite
│   └── NexusDesk.E2ETests/                       # Playwright End-to-End Browser Verification
└── infra/
    ├── terraform/                                # Azure Resource Provisioning (ACA/AKS, Postgres, Redis, KV)
    └── helm/                                     # Kubernetes Deployment, Ingress, HPA (2->6 pods)
```

---

## 2. Core Architectural Patterns

### 2.1 Multi-Tenant Resolution Pipeline
Every HTTP request passes through `TenantResolutionMiddleware`:
1. Extracts tenant identifier from `X-Tenant-Id` header, subdomain (`{tenant}.nexusdesk.local`), or JWT claim `tenant_id`.
2. Queries Redis cache (fallback to Master Catalog DB) for `TenantDescriptor` (`TenantId`, `Subdomain`, `Plan`, `Status`, `IsolationMode`, `EncryptedConnectionString`).
3. If `Status == Suspended`, short-circuits with `403 Forbidden` (`TenantSuspendedException`).
4. Populates scoped `ITenantContextAccessor`.
5. Configures `PartitionedRateLimiter` bucket keyed by `TenantId` with limits governed by `Plan` (`Free: 60/m`, `Pro: 600/m`, `Enterprise: 5000/m`).

### 2.2 Hybrid Database Connection Factory
`ITenantConnectionFactory` resolves the database connection per request:
* **Shared Mode (`Free`, `Pro`):** Returns the shared PostgreSQL pool connection and executes `SELECT set_config('app.current_tenant_id', @tenantId, true);` upon connection open.
* **Dedicated Mode (`Enterprise`):** Decrypts the tenant's dedicated connection string via `IDataProtector` and connects to their isolated database.

### 2.3 Event Sourcing & CQRS (Ticketing Module)
* **Write Model:** Commands (`CreateTicketCommand`, `AssignTicketCommand`, `AddReplyCommand`, `EscalateSlaCommand`) load the `TicketAggregate` from Marten's `mt_events` table, validate business rules, and append strongly-typed immutable events.
* **Read Model:** `TicketProjection` runs inline within the unit of work to update the `ticket_read_models` table, ensuring zero eventual-consistency lag for the immediate caller while publishing integration events to the transactional outbox.

### 2.4 High-Performance Hot Path (Source Generators & Span<T>)
To satisfy the BenchmarkDotNet performance requirement:
* All DTOs in `NexusDesk.Api` use `[JsonSerializable]` via `NexusDeskJsonContext : JsonSerializerContext` for zero-reflection compile-time UTF-8 serialization.
* High-frequency log statements use `[LoggerMessage]` compile-time source generators.
* CSV/Search token parsing and HMAC signature generation use `Span<byte>` and `ArrayPool<byte>.Shared` to achieve zero heap allocations.

---

## 3. Architecture Decision Records (ADRs)

| ADR ID | Decision | Context & Rationale |
| :--- | :--- | :--- |
| **ADR-001** | **Modular Monolith over Microservices** | Enables clean domain boundaries and single-deployment simplicity while allowing individual modules (like `WebhookSigner.Aot`) to be extracted independently. |
| **ADR-002** | **Hybrid Multi-Tenancy (RLS + Dedicated DB)** | Balances low infrastructure cost for Free/Pro tenants via PostgreSQL RLS with strict physical isolation for Enterprise tenants. |
| **ADR-003** | **Marten on PostgreSQL for Event Sourcing** | Consolidates relational tables, event streams (`mt_events`), and vector embeddings (`pgvector`) into a single PostgreSQL engine, avoiding operational overhead of managing separate EventStoreDB and Pinecone clusters. |
| **ADR-004** | **OpenIddict for Identity Provider** | Runs OAuth2/OIDC natively inside .NET 9 using the existing PostgreSQL cluster without requiring a heavy external Java Keycloak container. |
| **ADR-005** | **HotChocolate GraphQL + REST Hybrid API** | Provides predictable REST endpoints for commands/webhooks and flexible GraphQL selection sets for the complex Agent Dashboard ticket grid. |
| **ADR-006** | **SignalR with Redis Backplane** | Guarantees horizontal scale-out across 2–6 Kubernetes pods so customers and agents connected to different pods communicate seamlessly. |
| **ADR-007** | **Transactional Outbox for Webhooks & SLA** | Prevents dual-write bugs between database transactions and external HTTP/Service Bus dispatch. |
| **ADR-008** | **React 19 + TypeScript + Vite SPA** | Decouples frontend UI rendering from backend compute and provides rich interactive dashboards for Super Admins, Tenant Admins, Agents, and Customers. |

---

## 4. STRIDE Threat Model

| Threat Category | Target Surface | Mitigation Strategy Engineered in NexusDesk |
| :--- | :--- | :--- |
| **Spoofing** | Subdomain / Header Manipulation | `TenantResolutionMiddleware` cryptographically binds JWT `tenant_id` claim to the requested subdomain; mismatches return `403`. |
| **Tampering** | Ticket Audit History / Webhooks | Append-only event store (no UPDATE/DELETE privileges on `mt_events`); outbound webhooks signed via `HMAC-SHA256`. |
| **Repudiation** | Agent Actions / SLA Changes | EF Core `AuditSaveChangesInterceptor` and Marten event metadata record `ActorUserId`, `IpAddress`, and `CorrelationId` on every state change. |
| **Information Disclosure** | Cross-Tenant Data Leak / Prompt Injection | Defense-in-depth: Custom Roslyn Analyzer (`ND0001`) + EF Global Query Filter + Postgres RLS Policy + Vector Pre-Filtering + LLM Prompt Sanitizer. |
| **Denial of Service** | Noisy Neighbor API Flood | Per-tenant `PartitionedRateLimiter` enforcing sliding-window quotas by subscription tier before hitting database or AI pipelines. |
| **Elevation of Privilege** | Customer invoking Admin/Agent APIs | Policy-based authorization handlers (`RequireTenantRole`, `RequirePlanFeature`) validating fine-grained permission claims on every endpoint. |