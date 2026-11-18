# NexusDesk

**A multi-tenant B2B customer support and helpdesk platform**: a .NET 9 Clean Architecture backend paired with a React 19 single-page app.

NexusDesk hosts many isolated companies (tenants) on one platform. Most tenants share a PostgreSQL database protected by Row-Level Security. Enterprise tenants get their own physical database. Agents work tickets and live chats in real time, an AI assistant drafts grounded replies, and a SuperAdmin console runs the platform.

![.NET 9](https://img.shields.io/badge/.NET-9-512BD4) ![React 19](https://img.shields.io/badge/React-19-61DAFB) ![PostgreSQL 16](https://img.shields.io/badge/PostgreSQL-16-336791) ![License: MIT](https://img.shields.io/badge/License-MIT-green)

---

## Architecture

```
┌──────────────────────────── React 19 SPA (Vite, :5173) ────────────────────────────┐
│  TanStack Query · Zustand · @microsoft/signalr · Tailwind CSS v4                   │
└───────────────┬───────────────────────────────────────────────┬────────────────────┘
                │ REST /api/v1 · GraphQL                        │ WebSockets
                ▼                                               ▼
┌──────────────────────────── NexusDesk.Api (.NET 9, :56100) ────────────────────────┐
│  Tenant resolution middleware → JWT + RBAC → tenant-claim match → rate limiting    │
│  Modules: Tenants · Identity · Ticketing · Chat · KnowledgeBase · Automation ·     │
│           Billing · Analytics          SignalR hubs: /hubs/tickets · /hubs/chat    │
└───────────────┬───────────────────────────────────────────────┬────────────────────┘
                ▼                                               ▼
   nexusdesk_shared (PostgreSQL 16)                 nexusdesk_tenant_<slug>
   SharedDatabase tenants, isolated by              DedicatedDatabase tenants
   RLS on app.current_tenant_id                     (Enterprise, physical isolation)
```

### Hybrid tenant isolation

| Strategy | Who gets it | How isolation works |
| --- | --- | --- |
| `SharedDatabase` | Free / Pro tenants | All rows live in `nexusdesk_shared`. Every tenant table has `FORCE ROW LEVEL SECURITY` policies keyed on the session variable `app.current_tenant_id`, which an EF Core connection interceptor sets per request. |
| `DedicatedDatabase` | Enterprise tenants | The tenant gets its own physical database, `nexusdesk_tenant_<slug>`. Its connection string is stored encrypted with ASP.NET Core Data Protection and resolved per request. |

The backend is a **modular monolith in Clean Architecture style**. Each module (`src/Modules/*`) owns its domain, persistence, endpoints, and schema initializer. A shared kernel holds the cross-cutting multi-tenancy contracts, and architecture tests enforce the module boundaries.

---

## Key capabilities

- **Multi-tenant isolation and SuperAdmin console**: tenant provisioning, suspension, plan changes, and platform-wide tenant directory.
- **Real-time collaboration (SignalR)**:
  - `/hubs/tickets` for live presence and typing indicators on tickets.
  - `/hubs/chat` for omnichannel customer-to-agent live chat.
- **AI Ticket Assist**: reply drafts grounded in knowledge-base articles with citations, protected by an adversarial **Prompt Injection Guard**.
- **SLA policy engine**: per-priority first-response and resolution targets, with a background worker that detects breaches.
- **Automation rules**: event-driven ticket automation.
- **Reliable integrations**: a transactional outbox, signed webhooks, and a webhook dead-letter queue (DLQ) with retry.
- **Subscription billing and quotas**: plan tiers enforce agent seats, monthly ticket volume, and storage limits.
- **Audit logs** for tenant-level administrative actions.
- **Executive analytics**: dashboards with CSV export.
- **Customer self-service portal**: customers track their own tickets and can request **GDPR Article 17 erasure**.
- **Ticket attachments**, knowledge-base search, and GraphQL read APIs.

---

## Tech stack

| Layer | Technologies |
| --- | --- |
| **Backend** | .NET 9, ASP.NET Core minimal APIs, EF Core 9 (Npgsql), PostgreSQL 16 + pgvector, Redis, SignalR, HotChocolate GraphQL, JWT bearer auth + role-based access control, ASP.NET Core Data Protection, .NET Aspire service defaults (OpenTelemetry) |
| **Frontend** | React 19, TypeScript, Vite, Tailwind CSS v4, TanStack Query v5, Zustand, React Router v7, `@microsoft/signalr`, `lucide-react` |
| **Testing** | xUnit, FluentAssertions, ASP.NET Core integration tests against real PostgreSQL, architecture tests; Vitest + React Testing Library (unit, component, and live contract suites) |

---

## Local setup

### 1. Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/)
- [Docker](https://www.docker.com/) (recommended) **or** a local PostgreSQL 16 with the `pgvector` extension, plus Redis 7

### 2. Start PostgreSQL and Redis

From the repository root:

```bash
docker compose up -d
```

This starts:

| Service | Host port | Notes |
| --- | --- | --- |
| PostgreSQL 16 (pgvector) | `56132` | user `postgres` / password `postgres`, database `nexusdesk_shared` |
| Redis 7 | `56179` | tenant resolution cache and SignalR backplane |

### 3. Configuration

The defaults work with the Docker setup above, so no edits are needed.

- `src/NexusDesk.Api/appsettings.Development.json` holds the development connection strings (`ConnectionStrings:PostgreSql` and `ConnectionStrings:Redis`).
- `.env.example` documents the port allocations and database variables. Copy it to `.env` if you want to override them.
- If you use your own PostgreSQL, point `ConnectionStrings:PostgreSql` at it, or set `POSTGRES_HOST`, `NEXUSDESK_POSTGRES_PORT`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, and `POSTGRES_DB`.

> **Data Protection keys.** The API persists its Data Protection key ring to `src/NexusDesk.Api/.dataprotection-keys/`, a gitignored directory. Encrypted values, such as dedicated-tenant connection strings, therefore stay readable across restarts. If the keys are lost or replaced, the API re-encrypts each dedicated tenant's connection string on its next startup.

### 4. Run the backend API

```bash
dotnet run --project src/NexusDesk.Api/NexusDesk.Api.csproj --urls "http://localhost:56100"
```

On startup the API creates every schema, table, and RLS policy, and seeds the platform SuperAdmin. Swagger UI is at <http://localhost:56100/swagger>.

### 5. Provision the demo tenants (first run only)

The demo tenants are created through the provisioning API. With the API running:

```bash
curl -X POST http://localhost:56100/api/v1/tenants/provision -H "Content-Type: application/json" \
  -d '{"organizationName":"Acme Corporation","subdomain":"acme","adminEmail":"admin@acme.com","adminFullName":"Acme Admin","planTier":"Pro"}'

curl -X POST http://localhost:56100/api/v1/tenants/provision -H "Content-Type: application/json" \
  -d '{"organizationName":"Beta Innovations","subdomain":"beta","adminEmail":"admin@beta.com","adminFullName":"Beta Admin","planTier":"Free"}'

curl -X POST http://localhost:56100/api/v1/tenants/provision -H "Content-Type: application/json" \
  -d '{"organizationName":"Globex Global","subdomain":"globex","adminEmail":"admin@globex.com","adminFullName":"Globex Admin","planTier":"Enterprise"}'
```

`globex` is an Enterprise tenant, so it is provisioned into its own `nexusdesk_tenant_globex` database.

**Restart the API once** after provisioning. The startup seeders then create the demo users listed below inside each tenant. Running `dotnet test` once does the same provisioning as these three requests.

### 6. Run the frontend SPA

```bash
cd frontend
npm install
npm run dev
```

Open <http://localhost:5173>. The Vite dev server proxies `/api`, `/hubs`, and `/graphql` to the API on port `56100`.

### Demo login credentials

The login screen has one-click buttons for each account. Every account uses the password **`Password123!`**.

| Account | Role | Tenant | Plan | Isolation |
| --- | --- | --- | --- | --- |
| `superadmin@nexusdesk.com` | Platform SuperAdmin | *(platform scope)* | n/a | Platform catalog |
| `admin@acme.com` | Tenant Admin | `acme` | Pro | Shared database + RLS |
| `agent@acme.com` | Agent | `acme` | Pro | Shared database + RLS |
| `customer@acme.com` | Customer (self-service portal) | `acme` | Pro | Shared database + RLS |
| `admin@beta.com` | Tenant Admin | `beta` | Free | Shared database + RLS |
| `admin@globex.com` | Tenant Admin | `globex` | Enterprise | Dedicated database (`nexusdesk_tenant_globex`) |

API clients select a tenant with the `X-Tenant-Subdomain` header, for example `X-Tenant-Subdomain: globex`.

---

## Verification and test suites

| Suite | Command | What it covers |
| --- | --- | --- |
| Backend (unit, architecture, integration) | `dotnet test` (repository root) | Domain rules, module boundaries, and end-to-end API flows against real PostgreSQL, including RLS leak-proof tests and dedicated-database routing. **Requires PostgreSQL and Redis running.** |
| Frontend unit and component tests | `cd frontend && npm test` | Components, hooks, the API client, and SignalR handling, using mocked transports. |
| Live contract tests | `cd frontend && npm run test:live` | The real UI driven against the running backend and dev server: tenant switching, the `globex` dedicated database, ticketing, chat, and admin flows. **Requires the API on `:56100` and `npm run dev` on `:5173`.** |

To point the live suite at another URL, set `VITE_API_BASE_URL`; the default is `http://localhost:5173`.

---

## Repository layout

```
src/
  NexusDesk.Api/              ASP.NET Core host: pipeline, SignalR hubs, GraphQL, startup seeding
  NexusDesk.SharedKernel/     Multi-tenancy contracts, module abstractions
  NexusDesk.ServiceDefaults/  OpenTelemetry, health checks, resilience
  NexusDesk.AppHost/          .NET Aspire orchestration
  Modules/                    Tenants, Identity, Ticketing, Chat, KnowledgeBase,
                              Automation, Billing, Analytics
tests/                        Unit, Architecture, and Integration test projects
frontend/                     React 19 SPA
docs/                         PRD, SRS, design document, schema and API contracts
infra/docker/                 Database init scripts
```

Detailed specifications are in [`docs/`](docs/): the [product requirements](docs/product_requirements_document.md), the [software design document](docs/software_design_document.md), and the [frontend API contract](docs/frontend_api_contract.md).

---

## License

Released under the [MIT License](LICENSE). © 2026 Muhammad Yasir.
