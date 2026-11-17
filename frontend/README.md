# NexusDesk Frontend

React SPA for the NexusDesk multi-tenant helpdesk platform (.NET 9 backend). Milestone 1 covers the
app shell, authentication, tenant switching, and the SuperAdmin platform console.

Every route, header, enum string and JSON property follows [`docs/frontend_api_contract.md`](../docs/frontend_api_contract.md).

## Stack

| Concern | Choice |
| --- | --- |
| Build / dev server | Vite 8, TypeScript (strict) |
| UI | React 19, Tailwind CSS v4, `lucide-react` |
| Server state | `@tanstack/react-query` v5 |
| Client state | `zustand` (persisted auth session + active tenant) |
| Routing | `react-router-dom` v7 |
| Tests | Vitest + React Testing Library (jsdom) |

## Quick start

Prerequisites: Node 20+ and the backend running on `http://localhost:56100`. From the repo root:

```bash
docker compose up -d                                   # PostgreSQL + Redis
dotnet run --project src/NexusDesk.Api --launch-profile http
```

Then:

```bash
cd frontend
npm install
npm run dev          # http://localhost:5173
```

Sign in with one of the **one-click demo accounts** on the login screen (password `Password123!`):

| Account | Role | Tenant | Plan | Isolation |
| --- | --- | --- | --- | --- |
| `superadmin@nexusdesk.com` | SuperAdmin | *(platform)* | — | Shared catalog |
| `admin@acme.com` | TenantAdmin | `acme` | Pro | Shared DB + RLS |
| `agent@acme.com` | Agent | `acme` | Pro | Shared DB + RLS |
| `customer@acme.com` | Customer | `acme` | Pro | Shared DB + RLS |
| `admin@beta.com` | TenantAdmin | `beta` | Free | Shared DB + RLS |
| `admin@globex.com` | TenantAdmin | `globex` | Enterprise | Dedicated DB |

Once signed in, the account menu (top right) can switch between demo accounts in one click.

## How API calls are routed (localhost and ngrok)

By default the SPA calls the API **same-origin**: `VITE_API_BASE_URL` is empty and the Vite dev server
proxies `/api`, `/connect`, `/health`, `/hubs` (WebSockets included), `/graphql` and `/swagger` to
`VITE_PROXY_TARGET` (default `http://localhost:56100`).

As a result, a single tunnel to port 5173 serves the whole demo:

```bash
ngrok http 5173
```

There are no CORS issues and no wildcard DNS is needed. The tenant comes from the
`X-Tenant-Subdomain` header, not the hostname. Vite already allows `*.ngrok-free.app`, `*.ngrok.app`,
`*.ngrok.io` and `*.trycloudflare.com`.

To call the API directly instead (no proxy), set an absolute origin:

```bash
# .env.local
VITE_API_BASE_URL=http://localhost:56100
```

See [`.env.example`](.env.example).

## Features (Milestone 1)

### Typed API client (`src/lib/api.ts`)
- Adds `Authorization: Bearer <accessToken>` and `X-Tenant-Subdomain: <active tenant>` to every
  request. A single call can override the tenant (`tenant: 'beta'`), leave it out (`tenant: null`), or
  skip auth (`auth: false`).
- Converts every non-2xx response into a typed `ApiProblemError` with RFC 7807 fields: `status`,
  `title`, `detail`, and `code` (taken from `code`, or from `errorCode` if `code` is missing). ASP.NET
  validation `errors` are flattened into a readable message. The error is also shown as a toast
  unless the call passes `silent: true`.
- On `401`, makes one refresh attempt via `POST /connect/token` (`grant_type: "refresh_token"`,
  `client_id: "nexusdesk-spa"`). If that succeeds, the original request is replayed. If not, the
  session is cleared and the user is redirected to `/login?expired=1`. Concurrent 401s share one
  refresh.

### Authentication
- `/login` has a tenant picker (`Platform`, `acme`, `beta`, `globex` or a custom subdomain), the
  email/password form, and the demo quick-login panel.
- Tenant users **must** send `X-Tenant-Subdomain` at login; the backend answers `400 Invalid email or
  password` without it. The SuperAdmin logs in at platform scope with no header.
- `/register` handles customer self-registration (`POST /api/v1/users/customers/register`) and signs
  the new customer straight into the portal.

### Role-aware shell
- After login, each role lands on its own home page: SuperAdmin → `/platform/tenants`,
  TenantAdmin/Agent → `/dashboard`, Customer → `/portal`.
- Routes are protected by role. Visiting a route your role can't use sends you to your home page.
- The sidebar only shows what your role can use. Routes for Milestones 2 and 3 already exist and are
  tagged `M2`/`M3`; each one lists the endpoints it will use.
- The **live tenant and isolation badge** in the header comes from `GET /api/v1/tenant/current`. It
  shows the subdomain, plan, `SharedDatabase`/`DedicatedDatabase`, and the active physical database
  (e.g. `db:nexusdesk_tenant_globex`). Click it to see the RLS session tenant and membership details.
- SuperAdmins can switch the tenant scope from the badge. This only changes the
  `X-Tenant-Subdomain` header, so tenants can be inspected without signing in again.
- Light and dark themes are available, and the layout works on mobile.

### SuperAdmin console
- **`/platform/tenants`**: the tenant directory (`GET /api/v1/platform/tenants`).
  - Search, plus filters for status and plan.
  - Badges for status, plan and isolation, a quota summary, and stats tiles.
  - **Provision tenant** modal (`POST /api/v1/platform/tenants`). The subdomain is generated from
    the organization name, you pick a plan, and the result screen shows the portal URL and quotas.
  - **Details drawer** (`GET /api/v1/platform/tenants/{id}`) with tenant metadata, quotas, and an
    audit-log timeline.
  - **Kill-switch**: suspend or reactivate a tenant
    (`PATCH /api/v1/platform/tenants/{id}/status`). A reason is required before it will submit
    (one-click presets are offered).
- **`/platform/system`**: shows `GET /api/v1/system/info` (loaded modules, PostgreSQL/Redis status,
  runtime details), plus `/health/ready` (each check, with timings) and `/health/live`. Refreshes
  every 15 s.

## Scripts

| Command | What it does |
| --- | --- |
| `npm run dev` | Dev server on port 5173 with the API proxy |
| `npm run build` | Type-check (`tsc --noEmit`) + production build to `dist/` |
| `npm run typecheck` | Type-check only |
| `npm test` | Unit/component tests (mocked `fetch`) |
| `npm run test:live` | End-to-end tests of the real UI against the **running** backend through the dev proxy (read-only; needs the API on 56100 and `npm run dev`) |
| `npm run preview` | Serve the production build |

### Test coverage
- `src/lib/api.test.ts`: header injection (tenant, bearer token, per-call overrides, tenant
  switching), ProblemDetails parsing (`code`/`errorCode`, validation errors, non-JSON bodies),
  error toasts, and 401 → refresh → replay / logout.
- `src/pages/auth/LoginPage.test.tsx`: login with the tenant header, quick-login for SuperAdmin and
  Customer, inline ProblemDetails on bad credentials, role-based redirects, and the role-filtered
  sidebar.
- `src/pages/platform/TenantDirectoryPage.test.tsx`: directory rendering with badges and quotas,
  search, suspend with a required reason (checks the PATCH body and the list refresh), reactivate,
  and the error state.
- `src/test/e2e.live.test.tsx` (`test:live`): SuperAdmin login → live tenant list; switching scope
  to globex → dedicated-DB badge; Acme admin → shared-RLS badge; Globex admin login; rejected
  credentials.

> **Windows note:** `npm test` runs Vitest through `scripts/vitest.mjs`, which upper-cases the drive
> letter of the working directory. With a lowercase drive letter (`d:\` vs `D:\`), Vite loads modules
> twice and Vitest fails with "failed to find the current suite". The test config also passes
> `--no-experimental-webstorage` so that Node 25's built-in `localStorage` doesn't hide jsdom's.

## Project layout

```
src/
  lib/          api client, contract types, react-query hooks, navigation/roles, demo data
  stores/       zustand stores: auth (persisted), toast, theme
  components/
    ui/         Button, Card, Badge, Input/Select/Textarea/Field, Dialog (modal + drawer),
                Table, Popover/Menu, Toaster, Spinner/Skeleton/EmptyState/ErrorPanel
    layout/     AppShell (sidebar + header), TenantContextBadge, AccountMenu, Logo
    domain/     Plan/Status/Isolation/Role badges
  pages/
    auth/       LoginPage, RegisterPage, TenantPicker
    platform/   TenantDirectoryPage, ProvisionTenantDialog, TenantDetailsDrawer,
                StatusChangeDialog, SystemHealthPage
    DashboardPage, AccountPage, PlaceholderPage
  routes.tsx    route table + RequireAuth role guard
  test/         setup, fetch mock helpers, live e2e suite
```
