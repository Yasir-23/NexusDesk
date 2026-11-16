# NexusDesk Frontend API Contract & Integration Specification

**Target Version:** NexusDesk Backend v1.0.0 (ASP.NET Core 9.0)  
**Document Purpose:** Complete, 100% verified developer contract for building the React SPA without guessing any HTTP method, route, header, query parameter, enum string, or JSON property name.  
**Source of Truth:** Verified directly against live compiled backend binaries and runtime OpenAPI schema (`/swagger/v1/swagger.json`).

---

## 1. Base URLs, Tunnel/Ngrok Compatibility & Required Headers

### 1.1 Local & Production Base URLs
- **Backend API Base URL:** `http://localhost:56100`
- **Frontend SPA (Vite Dev Server):** `http://localhost:5173`
- **Live OpenAPI Spec:** `http://localhost:56100/swagger/v1/swagger.json`
- **Swagger UI:** `http://localhost:56100/swagger`
- **GraphQL Endpoint:** `http://localhost:56100/graphql`
- **SignalR Ticket Hub:** `http://localhost:56100/hubs/tickets`
- **SignalR Chat Hub:** `http://localhost:56100/hubs/chat`

---

### 1.2 Multi-Tenant Resolution & Ngrok / Tunnel Compatibility

In local development with DNS overrides, requests can resolve tenant context via hostnames (e.g., `acme.nexusdesk.local` or `beta.localhost:56100`).

However, when demonstrating or deploying via **ngrok**, **Cloudflare Tunnels**, or single-domain environments where wildcard subdomains are unavailable:
- The backend's `TenantResolutionMiddleware` inspects the custom **`X-Tenant-Subdomain`** HTTP header with first priority.
- Setting `X-Tenant-Subdomain: acme` immediately scopes database queries, Row-Level Security (RLS) policies, and connection routing to tenant `acme` across every endpoint.
- Setting `X-Tenant-Subdomain: globex` switches database connections to the dedicated database `nexusdesk_tenant_globex`.
- A frontend tenant switcher dropdown only needs to update the active `X-Tenant-Subdomain` header in the HTTP client (e.g., Axios default headers or Fetch wrapper).

**Tenant Resolution Precedence:**
1. `X-Tenant-Subdomain` request header (e.g. `acme`, `beta`, `globex`)
2. `X-Tenant-Id` request header (GUID string)
3. `Host` header subdomain (e.g. `acme.nexusdesk.local`, `acme.nexusdesk.com`)
4. Platform fallback (for SuperAdmin `/platform` operations)

---

### 1.3 Required HTTP Headers

| Header | Required For | Format / Value | Description |
| :--- | :--- | :--- | :--- |
| `Authorization` | All protected endpoints | `Bearer <jwt_access_token>` | Standard OAuth2 / JWT bearer token |
| `X-Tenant-Subdomain` | All tenant-scoped operations | String (e.g. `acme`, `beta`, `globex`) | Resolves tenant boundary on shared domains & tunnels |
| `Content-Type` | JSON request bodies | `application/json` | Required for POST/PUT/PATCH JSON bodies |
| `Content-Type` | Attachment uploads | `multipart/form-data` | Form field name: `file` |

---

### 1.4 RFC 7807 `ProblemDetails` Error Structure

All HTTP error responses (`400`, `401`, `403`, `404`, `409`, `422`, `429`, `500`) return `application/problem+json`:

```json
{
  "type": "https://nexusdesk.io/errors/plan-agent-limit-exceeded",
  "title": "Plan Agent Limit Exceeded",
  "status": 422,
  "detail": "Tenant 'acme' on plan 'Pro' has reached the maximum quota of 10 active agents.",
  "instance": "/api/v1/users/agents",
  "code": "PLAN_AGENT_LIMIT_EXCEEDED",
  "errorCode": "PLAN_AGENT_LIMIT_EXCEEDED",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

**Key Error Properties to Handle in Frontend Interceptors:**
- `status` (`number`): HTTP status code.
- `title` (`string`): Short summary of error category.
- `detail` (`string`): User-friendly explanation of why the action failed.
- `code` / `errorCode` (`string`): Machine-readable business error code (e.g. `PORTAL_TITLE_REQUIRED`, `PLAN_AGENT_LIMIT_EXCEEDED`, `PROMPT_INJECTION_DETECTED`, `TENANT_SUSPENDED`, `Ticket.NotFound`).

---

## 2. Seeded Demo Accounts & Tenant Matrix

**Default Password for all seeded accounts:** `Password123!`

| Tenant Subdomain | Organization Name | Plan Tier | Isolation Strategy | Physical Database | Email | Role | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| *(Platform)* | NexusDesk Platform | Enterprise | Shared Catalog | `nexusdesk_shared` | `superadmin@nexusdesk.com` | `SuperAdmin` | Platform Super Admin (Global oversight, tenant provisioning, suspend/activate) |
| `acme` | Acme Corporation | `Pro` | `SharedDatabase` (RLS) | `nexusdesk_shared` | `admin@acme.com` | `TenantAdmin` | Tenant Admin: SLA, Automation, Webhooks, Billing, Agent invites |
| `acme` | Acme Corporation | `Pro` | `SharedDatabase` (RLS) | `nexusdesk_shared` | `agent@acme.com` | `Agent` | Support Staff: Agent Ticketing, Chat, KB Authoring, AI Assist |
| `acme` | Acme Corporation | `Pro` | `SharedDatabase` (RLS) | `nexusdesk_shared` | `customer@acme.com` | `Customer` | End User: Customer Portal, Ticket submission, Live Chat |
| `beta` | Beta Innovations | `Free` | `SharedDatabase` (RLS) | `nexusdesk_shared` | `admin@beta.com` | `TenantAdmin` | Free tier admin (Max 2 agents, 100 tickets/mo, upgrade prompt testing) |
| `globex` | Globex Global | `Enterprise` | `DedicatedDatabase` | `nexusdesk_tenant_globex` | `admin@globex.com` | `TenantAdmin` | Enterprise dedicated database isolation demo |

### Plan Quotas & Feature Tiers

| Plan Tier | Max Agents | Max Tickets/Mo | Max Storage | Monthly Price | Isolation Strategy | Features Enabled |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Free` | 2 | 100 | 1 GB | \$0 | Shared Database (RLS) | Basic Ticketing, Public KB |
| `Pro` | 10 | 5,000 | 20 GB | \$49 / mo | Shared Database (RLS) | SLA Policies, Webhooks, AI Ticket Assist, Custom Branding |
| `Enterprise` | 100 | 100,000 | 100 GB | \$199 / mo | Dedicated Database | Unlimited Automation, Dedicated Physical Database, 24/7 SLA |

---

## 3. Complete Endpoint & Exact JSON Payload Reference

All endpoints serialized with standard `camelCase` naming conventions and string enum representations.

---

### Module 1: Auth, Identity, OAuth2 & Privacy

#### 1. Ergonomic JSON Login
- **Method & Route:** `POST /api/v1/auth/login`
- **Auth Required:** No
- **`X-Tenant-Subdomain` Required:** Recommended (optional for `superadmin@nexusdesk.com`)
- **Request Body:**
```json
{
  "email": "admin@acme.com",
  "password": "Password123!"
}
```
- **Response Body (`200 OK`):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "refreshToken": "48_char_random_base64url_string",
  "user": {
    "id": "22222222-2222-2222-2222-222222222221",
    "email": "admin@acme.com",
    "fullName": "Acme Admin",
    "role": "TenantAdmin",
    "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "subdomain": "acme"
  }
}
```

#### 2. Get Current Authenticated Profile
- **Method & Route:** `GET /api/v1/auth/me`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
{
  "id": "22222222-2222-2222-2222-222222222221",
  "email": "admin@acme.com",
  "fullName": "Acme Admin",
  "phoneNumber": "+1-800-555-0201",
  "role": "TenantAdmin",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "isActive": true,
  "permissions": [
    "tickets:read",
    "tickets:write",
    "tickets:assign",
    "tickets:delete",
    "users:read",
    "users:invite",
    "sla:read",
    "sla:write",
    "webhooks:read",
    "webhooks:write",
    "billing:read",
    "billing:write",
    "analytics:read",
    "kb:read",
    "kb:manage"
  ],
  "ssoProvider": null,
  "lastLoginAtUtc": "2026-11-16T18:00:00Z",
  "createdAtUtc": "2026-11-10T00:00:00Z"
}
```

#### 3. SSO Provider Exchange
- **Method & Route:** `POST /api/v1/auth/sso/{provider}` (supported: `google`, `microsoft`)
- **Auth Required:** No
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "idToken": "google_or_ms_jwt_token_string",
  "email": "sso.user@acme.com",
  "fullName": "SSO User",
  "phoneNumber": "+1-555-0199"
}
```
- **Response Body (`200 OK`):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "refreshToken": "48_char_random_base64url_string",
  "user": {
    "id": "77777777-7777-7777-7777-777777777777",
    "email": "sso.user@acme.com",
    "fullName": "SSO User",
    "phoneNumber": "+1-555-0199",
    "role": "Customer",
    "ssoProvider": "google",
    "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "subdomain": "acme"
  }
}
```

#### 4. OAuth2 / OpenID Connect Token Endpoint
- **Method & Route:** `POST /connect/token`
- **Auth Required:** No
- **Content-Type:** `application/json` or `application/x-www-form-urlencoded`
- **Request Body (Password Grant):**
```json
{
  "grant_type": "password",
  "username": "admin@acme.com",
  "password": "Password123!",
  "client_id": "nexusdesk-spa"
}
```
- **Request Body (Refresh Token Grant):**
```json
{
  "grant_type": "refresh_token",
  "refresh_token": "48_char_random_base64url_string",
  "client_id": "nexusdesk-spa"
}
```
- **Request Body (PKCE Authorization Code Grant):**
```json
{
  "grant_type": "authorization_code",
  "code": "auth_code_from_authorize_endpoint",
  "code_verifier": "pkce_code_verifier_string",
  "client_id": "nexusdesk-spa"
}
```
- **Response Body (`200 OK`):**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "48_char_random_base64url_string",
  "scope": "openid profile email"
}
```

#### 5. GDPR Privacy Self-Erasure ("Forget Me")
- **Method & Route:** `POST /api/v1/privacy/forget-me`
- **Auth Required:** Bearer Token (Authenticated Tenant Customer/User; SuperAdmin is protected)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
{
  "anonymized": true,
  "userId": "22222222-2222-2222-2222-222222222223",
  "anonymizedAtUtc": "2026-11-16T18:30:00Z"
}
```

---

### Module 2: Platform & Tenant Administration

#### 1. Provision New Tenant
- **Method & Route:** `POST /api/v1/platform/tenants` (or `POST /api/v1/tenants/provision`)
- **Auth Required:** Public / SuperAdmin
- **Request Body:**
```json
{
  "organizationName": "Delta Logistics",
  "subdomain": "delta",
  "adminEmail": "admin@delta.com",
  "adminFullName": "Delta Admin",
  "planTier": "Pro",
  "adminPassword": "Password123!"
}
```
- **Response Body (`201 Created`):**
```json
{
  "tenantId": "8b5e9f12-3456-4789-abcd-0123456789ab",
  "organizationName": "Delta Logistics",
  "subdomain": "delta",
  "portalUrl": "https://delta.nexusdesk.local",
  "status": "Active",
  "isolationStrategy": "SharedDatabase",
  "planTier": "Pro",
  "planLimits": {
    "maxAgents": 10,
    "maxTicketsPerMonth": 5000,
    "maxStorageBytes": 21474836480
  },
  "createdAtUtc": "2026-11-16T18:30:00Z"
}
```

#### 2. List All Platform Tenants
- **Method & Route:** `GET /api/v1/platform/tenants?page=1&pageSize=20` (or `GET /api/v1/admin/tenants`)
- **Auth Required:** Bearer Token (`SuperAdmin` only)
- **Response Body (`200 OK`):**
```json
{
  "page": 1,
  "pageSize": 20,
  "totalCount": 3,
  "items": [
    {
      "id": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
      "name": "Acme Corporation",
      "subdomain": "acme",
      "planTier": "Pro",
      "isolationStrategy": "SharedDatabase",
      "status": "Active",
      "maxAgents": 10,
      "maxTicketsPerMonth": 5000,
      "maxStorageBytes": 21474836480,
      "createdAtUtc": "2026-11-10T00:00:00Z"
    }
  ]
}
```

#### 3. Get Tenant Details & Audit Logs
- **Method & Route:** `GET /api/v1/platform/tenants/{tenantId}`
- **Auth Required:** Bearer Token (`SuperAdmin` only)
- **Response Body (`200 OK`):**
```json
{
  "tenant": {
    "id": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "name": "Acme Corporation",
    "subdomain": "acme",
    "planTier": "Pro",
    "isolationStrategy": "SharedDatabase",
    "status": "Active",
    "maxAgents": 10,
    "maxTicketsPerMonth": 5000,
    "maxStorageBytes": 21474836480,
    "createdAtUtc": "2026-11-10T00:00:00Z",
    "updatedAtUtc": null
  },
  "auditLogs": [
    {
      "id": "e0a1b2c3-d4e5-f6a7-b8c9-d0e1f2a3b4c5",
      "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
      "action": "Provisioned",
      "reason": "Tenant 'acme' provisioned with plan 'Pro' and isolation 'SharedDatabase'.",
      "performedBy": "System",
      "timestampUtc": "2026-11-10T00:00:00Z"
    }
  ]
}
```

#### 4. Suspend or Reactivate Tenant
- **Method & Route:** `PATCH /api/v1/platform/tenants/{tenantId}/status`
- **Auth Required:** Bearer Token (`SuperAdmin` only)
- **Request Body:**
```json
{
  "status": "Suspended",
  "reason": "Overdue invoice non-payment"
}
```
- **Response Body (`200 OK`):**
```json
{
  "id": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "subdomain": "acme",
  "status": "Suspended",
  "updatedAtUtc": "2026-11-16T18:35:00Z"
}
```

#### 5. Verify Current Tenant Context & Connection Routing
- **Method & Route:** `GET /api/v1/tenant/current`
- **Auth Required:** No (Probes active request context)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
{
  "tenantContext": {
    "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "subdomain": "acme",
    "planTier": "Pro",
    "isolationStrategy": "SharedDatabase",
    "status": "Active",
    "isResolved": true
  },
  "activeDatabase": "nexusdesk_shared",
  "rlsSessionTenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "settingsCount": 1,
  "settings": [
    {
      "id": "f1e2d3c4-b5a6-9788-1234-567890abcdef",
      "key": "General.Theme",
      "value": "SystemDefault",
      "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4"
    }
  ],
  "membershipsCount": 3,
  "memberships": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "userId": "22222222-2222-2222-2222-222222222221",
      "role": "TenantAdmin",
      "email": "admin@acme.com",
      "fullName": "Acme Admin",
      "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4"
    }
  ]
}
```

#### 6. Create Agent (Enforces Plan Quotas)
- **Method & Route:** `POST /api/v1/users/agents`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "email": "sarah.agent@acme.com",
  "fullName": "Sarah Connor",
  "password": "Password123!",
  "phoneNumber": "+1-800-555-0299"
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "55555555-5555-5555-5555-555555555555",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "email": "sarah.agent@acme.com",
  "fullName": "Sarah Connor",
  "phoneNumber": "+1-800-555-0299",
  "role": "Agent",
  "isActive": true,
  "createdAtUtc": "2026-11-16T18:40:00Z"
}
```
*(Returns `422 UnprocessableEntity` with `PLAN_AGENT_LIMIT_EXCEEDED` if agent quota is exhausted)*.

#### 7. List Tenant Users
- **Method & Route:** `GET /api/v1/users`
- **Auth Required:** Bearer Token (`Agent`, `TenantAdmin`, or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
[
  {
    "id": "22222222-2222-2222-2222-222222222221",
    "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "email": "admin@acme.com",
    "fullName": "Acme Admin",
    "phoneNumber": "+1-800-555-0201",
    "role": "TenantAdmin",
    "isActive": true,
    "isAnonymized": false,
    "lastLoginAtUtc": "2026-11-16T18:00:00Z",
    "createdAtUtc": "2026-11-10T00:00:00Z"
  }
]
```

#### 8. Register Customer (Public Self-Registration)
- **Method & Route:** `POST /api/v1/users/customers/register`
- **Auth Required:** No
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "email": "new.customer@gmail.com",
  "fullName": "Jane Doe",
  "password": "Password123!",
  "phoneNumber": "+1-555-0144"
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "99999999-9999-9999-9999-999999999999",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "email": "new.customer@gmail.com",
  "fullName": "Jane Doe",
  "role": "Customer",
  "createdAtUtc": "2026-11-16T18:45:00Z"
}
```

---

### Module 3: Agent Ticketing & File Attachments

#### 1. Create Ticket (Agent or API)
- **Method & Route:** `POST /api/v1/tickets`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "title": "Cannot access billing portal from Chrome",
  "description": "Receiving a blank page and 403 Forbidden after MFA prompt.",
  "priority": "High",
  "channel": "WebPortal",
  "customerId": "22222222-2222-2222-2222-222222222223",
  "tags": ["billing", "auth", "chrome"]
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "ticketNumber": "ND-10001",
  "title": "Cannot access billing portal from Chrome",
  "description": "Receiving a blank page and 403 Forbidden after MFA prompt.",
  "status": "New",
  "priority": "High",
  "channel": "WebPortal",
  "customerId": "22222222-2222-2222-2222-222222222223",
  "assignedAgentId": null,
  "resolutionNotes": null,
  "firstResponseDueUtc": "2026-11-16T19:45:00Z",
  "resolutionDueUtc": "2026-11-17T02:45:00Z",
  "firstRespondedAtUtc": null,
  "resolvedAtUtc": null,
  "closedAtUtc": null,
  "createdAtUtc": "2026-11-16T18:45:00Z",
  "updatedAtUtc": null,
  "rowVersion": 1,
  "tags": ["billing", "auth", "chrome"],
  "comments": [],
  "activityLogs": [
    {
      "id": "11111111-2222-3333-4444-555555555555",
      "performedByUserId": "22222222-2222-2222-2222-222222222223",
      "action": "Created",
      "oldValue": null,
      "newValue": "New",
      "timestampUtc": "2026-11-16T18:45:00Z"
    }
  ],
  "isSlaBreached": false,
  "slaBreachedAtUtc": null
}
```

#### 2. Get Paginated Tickets
- **Method & Route:** `GET /api/v1/tickets?status=Open&priority=High&page=1&pageSize=20`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Query Parameters:** `status` (enum string), `priority` (enum string), `assignedAgentId` (uuid), `customerId` (uuid), `page` (int), `pageSize` (int)
- **Response Body (`200 OK`):**
```json
{
  "items": [
    {
      "id": "652f5d1c-aed3-426d-aad5-48273c5140ad",
      "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
      "ticketNumber": "ND-10001",
      "title": "Cannot access billing portal from Chrome",
      "status": "Open",
      "priority": "High",
      "channel": "WebPortal",
      "customerId": "22222222-2222-2222-2222-222222222223",
      "assignedAgentId": "22222222-2222-2222-2222-222222222222",
      "firstResponseDueUtc": "2026-11-16T19:45:00Z",
      "resolutionDueUtc": "2026-11-17T02:45:00Z",
      "firstRespondedAtUtc": "2026-11-16T18:50:00Z",
      "resolvedAtUtc": null,
      "closedAtUtc": null,
      "createdAtUtc": "2026-11-16T18:45:00Z",
      "updatedAtUtc": "2026-11-16T18:50:00Z",
      "rowVersion": 2,
      "tags": ["billing", "auth", "chrome"],
      "isSlaBreached": false,
      "slaBreachedAtUtc": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

#### 3. Get Ticket Details by ID
- **Method & Route:** `GET /api/v1/tickets/{id}`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):** Full `TicketDetailsResponse` with `comments` and `activityLogs`.

#### 4. Assign Ticket to Agent
- **Method & Route:** `PATCH /api/v1/tickets/{id}/assign`
- **Auth Required:** Bearer Token (`Agent` or `TenantAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "agentId": "22222222-2222-2222-2222-222222222222",
  "rowVersion": 1
}
```
- **Response Body (`200 OK`):** Updated `TicketDetailsResponse`.

#### 5. Update Ticket Status
- **Method & Route:** `PATCH /api/v1/tickets/{id}/status`
- **Auth Required:** Bearer Token (`Agent` or `TenantAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "status": "Resolved",
  "resolutionNotes": "Cleared cached session on auth server.",
  "reason": "Issue verified resolved with customer",
  "rowVersion": 2
}
```
- **Response Body (`200 OK`):** Updated `TicketDetailsResponse`.

#### 6. Update Ticket Priority
- **Method & Route:** `PATCH /api/v1/tickets/{id}/priority`
- **Auth Required:** Bearer Token (`Agent` or `TenantAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "priority": "Urgent",
  "rowVersion": 3
}
```
- **Response Body (`200 OK`):** Updated `TicketDetailsResponse`.

#### 7. Add Ticket Comment
- **Method & Route:** `POST /api/v1/tickets/{id}/comments`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "body": "We have pushed a patch to resolve session token expiry.",
  "type": "PublicReply"
}
```
*(Or `"type": "InternalNote"` for private agent-to-agent collaboration)*.
- **Response Body (`201 Created`):**
```json
{
  "id": "77889900-1122-3344-5566-778899aabbcc",
  "authorUserId": "22222222-2222-2222-2222-222222222222",
  "isCustomer": false,
  "type": "PublicReply",
  "body": "We have pushed a patch to resolve session token expiry.",
  "createdAtUtc": "2026-11-16T18:55:00Z"
}
```

#### 8. Upload Ticket Attachment
- **Method & Route:** `POST /api/v1/tickets/{id}/attachments`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Content-Type:** `multipart/form-data`
- **Form Field Name:** `file` (Binary file stream; verified with magic-byte checking; max 10MB)
- **Allowed Extensions:** `.png`, `.jpg`, `.jpeg`, `.pdf`, `.txt`, `.log`, `.csv`, `.zip`
- **Response Body (`201 Created`):**
```json
{
  "id": "b64ed58a-3432-439a-8112-90147fd8f53d",
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "uploadedByUserId": "22222222-2222-2222-2222-222222222222",
  "fileName": "screenshot_error_403.png",
  "contentType": "image/png",
  "sizeBytes": 245102,
  "createdAtUtc": "2026-11-16T19:00:00Z"
}
```

#### 9. List Ticket Attachments
- **Method & Route:** `GET /api/v1/tickets/{id}/attachments`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
[
  {
    "id": "b64ed58a-3432-439a-8112-90147fd8f53d",
    "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
    "uploadedByUserId": "22222222-2222-2222-2222-222222222222",
    "fileName": "screenshot_error_403.png",
    "contentType": "image/png",
    "sizeBytes": 245102,
    "createdAtUtc": "2026-11-16T19:00:00Z"
  }
]
```

#### 10. Download Ticket Attachment Stream
- **Method & Route:** `GET /api/v1/tickets/{id}/attachments/{attachmentId}/download`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response:** Raw binary file stream with `Content-Disposition: attachment; filename="..."`.

---

### Module 4: Customer Portal (`/api/v1/portal`)

#### 1. List Customer's Tickets
- **Method & Route:** `GET /api/v1/portal/tickets?status=Open&page=1&pageSize=20`
- **Auth Required:** Bearer Token (Strictly filtered to authenticated customer ID)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):** `PagedTicketsResponse`.

#### 2. Create Ticket from Customer Portal
- **Method & Route:** `POST /api/v1/portal/tickets`
- **Auth Required:** Bearer Token (`Customer`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "title": "Need help setting up SSO integration",
  "description": "Where do I find the metadata XML file for Azure AD?",
  "priority": "Medium",
  "tags": ["sso", "azure-ad"]
}
```
- **Response Body (`201 Created`):** `TicketDetailsResponse`.

#### 3. Get Customer Ticket Details
- **Method & Route:** `GET /api/v1/portal/tickets/{id}`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Behavior:** Returns `404 Not Found` if ticket belongs to another customer. Automatically scrubs all `InternalNote` comments and `activityLogs`.

#### 4. Add Customer Reply
- **Method & Route:** `POST /api/v1/portal/tickets/{id}/replies`
- **Auth Required:** Bearer Token (`Customer`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "message": "Thank you, I found the file and configured the claims!"
}
```
- **Response Body (`201 Created`):** `TicketCommentDto`.

---

### Module 5: Omnichannel Live Chat

#### 1. Start Chat Session (Customer)
- **Method & Route:** `POST /api/v1/chat/sessions`
- **Auth Required:** Bearer Token (`Customer`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "subject": "Pre-sales inquiry regarding Enterprise dedicated database"
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "33445566-7788-99aa-bbcc-ddeeff001122",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "customerId": "22222222-2222-2222-2222-222222222223",
  "assignedAgentId": null,
  "subject": "Pre-sales inquiry regarding Enterprise dedicated database",
  "status": "Waiting",
  "convertedTicketId": null,
  "createdAtUtc": "2026-11-16T19:10:00Z",
  "closedAtUtc": null,
  "messages": []
}
```

#### 2. List Chat Sessions
- **Method & Route:** `GET /api/v1/chat/sessions?status=Waiting`
- **Auth Required:** Bearer Token (`Agent` sees all tenant sessions; `Customer` sees only their own)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):** Array of `ChatSessionSummaryDto`.

#### 3. Get Chat Session by ID
- **Method & Route:** `GET /api/v1/chat/sessions/{id}`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):** `ChatSessionDto` with chronological `messages`.

#### 4. Agent Claim Waiting Session
- **Method & Route:** `POST /api/v1/chat/sessions/{id}/claim`
- **Auth Required:** Bearer Token (`Agent` or `TenantAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):** Updated `ChatSessionDto` with `status: "Active"`.

#### 5. Send Chat Message
- **Method & Route:** `POST /api/v1/chat/sessions/{id}/messages`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "content": "Hello! I would be glad to help answer your questions about Enterprise isolation."
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "8899aabb-ccdd-eeff-0011-223344556677",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "chatSessionId": "33445566-7788-99aa-bbcc-ddeeff001122",
  "senderUserId": "22222222-2222-2222-2222-222222222222",
  "senderRole": "Agent",
  "content": "Hello! I would be glad to help answer your questions about Enterprise isolation.",
  "sentAtUtc": "2026-11-16T19:12:00Z"
}
```

#### 6. Close Chat Session (Optional Conversion to Ticket)
- **Method & Route:** `POST /api/v1/chat/sessions/{id}/close`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "convertToTicket": true
}
```
- **Response Body (`200 OK`):**
```json
{
  "id": "33445566-7788-99aa-bbcc-ddeeff001122",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "status": "ConvertedToTicket",
  "convertedTicketId": "99887766-5544-3322-1100-ffeeddccbbaa",
  "closedAtUtc": "2026-11-16T19:20:00Z"
}
```

---

### Module 6: SLA Management, Automation & Webhooks/Outbox/DLQ

#### 1. Upsert SLA Policy
- **Method & Route:** `PUT /api/v1/tenant-admin/sla-policies`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "priority": "Urgent",
  "firstResponseTargetMinutes": 15,
  "resolutionTargetMinutes": 120
}
```
- **Response Body (`200 OK`):**
```json
{
  "id": "12121212-3434-5656-7878-909090909090",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "priority": "Urgent",
  "firstResponseTargetMinutes": 15,
  "resolutionTargetMinutes": 120,
  "createdAtUtc": "2026-11-10T00:00:00Z",
  "updatedAtUtc": "2026-11-16T19:25:00Z"
}
```

#### 2. List SLA Policies
- **Method & Route:** `GET /api/v1/tenant-admin/sla-policies`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):** Array of `SlaPolicyResponse`.

#### 3. Create Automation Rule
- **Method & Route:** `POST /api/v1/tenant-admin/automation-rules`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "name": "Auto-escalate VIP billing issues",
  "triggerEvent": "ticket.created",
  "conditionJson": "{\"priority\":\"High\",\"tags\":[\"billing\"]}",
  "actionType": "assign_to_tier2",
  "actionPayloadJson": "{\"targetTeamId\":\"tier2-billing\"}",
  "isEnabled": true
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "45454545-6767-8989-abab-cdcdcdcdcdcd",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "name": "Auto-escalate VIP billing issues",
  "triggerEvent": "ticket.created",
  "conditionJson": "{\"priority\":\"High\",\"tags\":[\"billing\"]}",
  "actionType": "assign_to_tier2",
  "actionPayloadJson": "{\"targetTeamId\":\"tier2-billing\"}",
  "isEnabled": true,
  "createdAtUtc": "2026-11-16T19:30:00Z",
  "updatedAtUtc": null
}
```

#### 4. Toggle Automation Rule
- **Method & Route:** `PATCH /api/v1/tenant-admin/automation-rules/{id}/toggle`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "isEnabled": false
}
```
- **Response Body (`200 OK`):** Updated `AutomationRuleResponse`.

#### 5. Register Outbound Webhook Subscription
- **Method & Route:** `POST /api/v1/tenant-admin/webhooks`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "targetUrl": "https://webhook.site/test-receiver",
  "secret": "whsec_super_secret_signing_key_12345",
  "eventTypes": ["ticket.created", "ticket.sla_breached", "ticket.resolved"],
  "isActive": true
}
```
- **Response Body (`201 Created`):**
```json
{
  "id": "78787878-9090-abab-cdef-121212121212",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "targetUrl": "https://webhook.site/test-receiver",
  "eventTypes": ["ticket.created", "ticket.sla_breached", "ticket.resolved"],
  "isActive": true,
  "createdAtUtc": "2026-11-16T19:35:00Z",
  "updatedAtUtc": null
}
```

#### 6. Inspect Outbox & Dead-Letter Queue (DLQ)
- **Get Outbox Queue:** `GET /api/v1/tenant-admin/webhooks/outbox`
  - Returns array of `OutboxMessageResponse` (`status`: `Pending`, `Dispatched`, `Failed`).
- **Get Dead-Letter Queue:** `GET /api/v1/tenant-admin/webhooks/dead-letters`
  - Returns array of `DeadLetterWebhookResponse` with `attemptCount`, `lastHttpStatusCode`, and `lastError`.

---

### Module 7: Knowledge Base & AI Assist

#### 1. List Categories
- **Method & Route:** `GET /api/v1/kb/categories`
- **Auth Required:** No (Public / Customer / Staff)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
[
  {
    "id": "10101010-2020-3030-4040-505050505050",
    "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "name": "Getting Started",
    "slug": "getting-started",
    "description": "Guides and tutorials for new users",
    "displayOrder": 1,
    "createdAtUtc": "2026-11-10T00:00:00Z",
    "publishedArticlesCount": 4
  }
]
```

#### 2. Create Article
- **Method & Route:** `POST /api/v1/kb/articles`
- **Auth Required:** Bearer Token (`Agent`, `TenantAdmin`, or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "categoryId": "10101010-2020-3030-4040-505050505050",
  "title": "Configuring Single Sign-On with Okta",
  "slug": "configuring-sso-okta",
  "summary": "Step-by-step setup guide for SAML 2.0 and OIDC in Okta.",
  "bodyMarkdown": "# Okta Setup\n\nNavigate to Applications...",
  "tags": ["sso", "okta", "security"],
  "publishImmediately": true
}
```
- **Response Body (`201 Created`):** `KbArticleDto`.

#### 3. Deflection Search (Hybrid Full-Text + Semantic Vector)
- **Method & Route:** `GET /api/v1/kb/search?q=cannot+login+to+sso&limit=5`
- **Auth Required:** No
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
[
  {
    "id": "60606060-7070-8080-9090-a0a0a0a0a0a0",
    "title": "Troubleshooting SSO Login Failures",
    "slug": "troubleshooting-sso-login-failures",
    "summary": "Fix common SAML token and clock-skew errors.",
    "categoryName": "Security",
    "relevanceScore": 0.94,
    "helpfulCount": 42
  }
]
```

#### 4. AI Grounded Draft Reply Suggestion
- **Method & Route:** `POST /api/v1/ai/tickets/{ticketId}/suggest-reply`
- **Auth Required:** Bearer Token (`Agent` or `TenantAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "customInstructions": "Maintain a warm, polite tone and reference the SSO setup guide."
}
```
- **Response Body (`200 OK`):**
```json
{
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "suggestedReply": "Hello,\n\nThank you for reaching out regarding 'Cannot access billing portal'. Based on our knowledge base, please refer to the following guide: 'Troubleshooting SSO Login Failures'. If you need further assistance, please let us know.\n\nBest regards,\nSupport Team",
  "referencedArticles": [
    {
      "id": "60606060-7070-8080-9090-a0a0a0a0a0a0",
      "title": "Troubleshooting SSO Login Failures",
      "slug": "troubleshooting-sso-login-failures"
    }
  ]
}
```

#### 5. AI Ticket Thread Executive Summary
- **Method & Route:** `POST /api/v1/ai/tickets/{ticketId}/summarize`
- **Auth Required:** Bearer Token (`Agent` or `TenantAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "customInstructions": "Highlight root cause and pending customer actions."
}
```
- **Response Body (`200 OK`):**
```json
{
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "summary": "Ticket ND-10001 ('Cannot access billing portal'): Receiving a blank page and 403 Forbidden after MFA prompt. Thread includes 2 discussion comment(s).",
  "keyPoints": [
    "Subject: Cannot access billing portal from Chrome",
    "Initial Report: Receiving a blank page and 403 Forbidden after MFA prompt.",
    "Latest activity: We have pushed a patch to resolve session token expiry."
  ],
  "commentCount": 2
}
```

---

### Module 8: Multi-Tenant Billing, Plans & Quotas

#### 1. Public Plan Catalog
- **Method & Route:** `GET /api/v1/billing/plans`
- **Auth Required:** No
- **Response Body (`200 OK`):**
```json
[
  {
    "id": "b1111111-0000-0000-0000-000000000001",
    "code": "Free",
    "name": "Free Starter",
    "monthlyPriceCents": 0,
    "maxAgents": 2,
    "maxMonthlyTickets": 100,
    "maxMonthlyAiTokens": 10000,
    "customBrandingEnabled": false,
    "webhooksEnabled": false,
    "slaEnabled": false,
    "isActive": true
  },
  {
    "id": "b1111111-0000-0000-0000-000000000002",
    "code": "Pro",
    "name": "Professional Team",
    "monthlyPriceCents": 4900,
    "maxAgents": 10,
    "maxMonthlyTickets": 5000,
    "maxMonthlyAiTokens": 500000,
    "customBrandingEnabled": true,
    "webhooksEnabled": true,
    "slaEnabled": true,
    "isActive": true
  },
  {
    "id": "b1111111-0000-0000-0000-000000000003",
    "code": "Enterprise",
    "name": "Enterprise Dedicated",
    "monthlyPriceCents": 19900,
    "maxAgents": 100,
    "maxMonthlyTickets": 100000,
    "maxMonthlyAiTokens": 5000000,
    "customBrandingEnabled": true,
    "webhooksEnabled": true,
    "slaEnabled": true,
    "isActive": true
  }
]
```

#### 2. Get Current Tenant Subscription & Usage Meter
- **Method & Route:** `GET /api/v1/billing/subscription`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
{
  "subscription": {
    "id": "90909090-8080-7070-6060-505050505050",
    "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
    "planCode": "Pro",
    "status": "Active",
    "billingCycle": "Monthly",
    "stripeCustomerId": "cus_test_acme_123",
    "stripeSubscriptionId": "sub_test_acme_456",
    "currentPeriodStartUtc": "2026-11-01T00:00:00Z",
    "currentPeriodEndUtc": "2026-12-01T00:00:00Z",
    "cancelAtPeriodEnd": false,
    "updatedAtUtc": "2026-11-01T00:00:00Z"
  },
  "plan": {
    "id": "b1111111-0000-0000-0000-000000000002",
    "code": "Pro",
    "name": "Professional Team",
    "monthlyPriceCents": 4900,
    "maxAgents": 10,
    "maxMonthlyTickets": 5000,
    "maxMonthlyAiTokens": 500000,
    "customBrandingEnabled": true,
    "webhooksEnabled": true,
    "slaEnabled": true,
    "isActive": true
  },
  "periodKey": "2026-11",
  "usage": [
    {
      "metricName": "active_agents",
      "used": 3,
      "limit": 10,
      "remaining": 7,
      "usagePercentage": 30.0,
      "remainingPercentage": 70.0
    },
    {
      "metricName": "tickets_created",
      "used": 142,
      "limit": 5000,
      "remaining": 4858,
      "usagePercentage": 2.84,
      "remainingPercentage": 97.16
    },
    {
      "metricName": "ai_tokens_used",
      "used": 28400,
      "limit": 500000,
      "remaining": 471600,
      "usagePercentage": 5.68,
      "remainingPercentage": 94.32
    }
  ]
}
```

#### 3. Upgrade / Change Subscription Plan
- **Method & Route:** `POST /api/v1/billing/subscription/change-plan`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Request Body:**
```json
{
  "planCode": "Enterprise",
  "billingCycle": "Monthly"
}
```
- **Response Body (`200 OK`):** `TenantSubscriptionDetailsDto`.

---

### Module 9: Analytics, Audit Logs & In-App Notifications

#### 1. Executive Analytics Overview
- **Method & Route:** `GET /api/v1/analytics/overview?days=30`
- **Auth Required:** Bearer Token (`Agent`, `TenantAdmin`, or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
{
  "totalTickets": 248,
  "openTickets": 18,
  "resolvedTickets": 230,
  "slaComplianceRate": 98.4,
  "averageFirstResponseMinutes": 14.2,
  "averageResolutionMinutes": 96.5,
  "csatAverageScore": 4.85,
  "activeChatSessions": 3,
  "publishedKbArticles": 28
}
```

#### 2. Daily Ticket Trends
- **Method & Route:** `GET /api/v1/analytics/tickets/trends?days=7`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
[
  {
    "date": "2026-11-10",
    "createdCount": 35,
    "resolvedCount": 32,
    "breachedCount": 1
  },
  {
    "date": "2026-11-11",
    "createdCount": 40,
    "resolvedCount": 38,
    "breachedCount": 0
  }
]
```

#### 3. Agent Performance Scoreboard
- **Method & Route:** `GET /api/v1/analytics/agents/performance?days=30`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
[
  {
    "agentUserId": "22222222-2222-2222-2222-222222222222",
    "agentName": "Acme Agent",
    "assignedTickets": 115,
    "resolvedTickets": 112,
    "averageResolutionMinutes": 84.1,
    "slaComplianceRate": 99.1
  }
]
```

#### 4. Export Tickets CSV
- **Method & Route:** `GET /api/v1/analytics/export/tickets.csv?days=30`
- **Auth Required:** Bearer Token
- **`X-Tenant-Subdomain` Required:** Yes
- **Response:** Raw text stream (`text/csv; charset=utf-8`) with header:
  `TicketNumber,Title,Status,Priority,Category,CustomerEmail,AssignedAgentEmail,IsSlaBreached,CreatedAtUtc,ResolvedAtUtc`

#### 5. Query Audit Logs
- **Method & Route:** `GET /api/v1/audit-logs?actorEmail=admin@acme.com&page=1&pageSize=50`
- **Auth Required:** Bearer Token (`TenantAdmin` or `SuperAdmin`)
- **`X-Tenant-Subdomain` Required:** Yes
- **Response Body (`200 OK`):**
```json
{
  "items": [
    {
      "id": "e4e4e4e4-5555-6666-7777-888888888888",
      "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
      "actorUserId": "22222222-2222-2222-2222-222222222221",
      "actorEmail": "admin@acme.com",
      "action": "sla.upsert",
      "resourceType": "sla_policy",
      "resourceId": "12121212-3434-5656-7878-909090909090",
      "metadataJson": "{\"priority\":\"Urgent\",\"firstResponse\":15}",
      "ipAddress": "127.0.0.1",
      "createdAtUtc": "2026-11-16T19:25:00Z"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 1,
  "totalPages": 1
}
```

#### 6. User In-App Notifications
- **List Notifications:** `GET /api/v1/notifications?unreadOnly=true&page=1&pageSize=20`
```json
{
  "items": [
    {
      "id": "91919191-2323-4545-6767-898989898989",
      "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
      "recipientUserId": "22222222-2222-2222-2222-222222222222",
      "type": "ticket.assigned",
      "title": "New Ticket Assigned",
      "message": "Ticket ND-10001 has been assigned to you.",
      "linkUrl": "/tickets/652f5d1c-aed3-426d-aad5-48273c5140ad",
      "isRead": false,
      "readAtUtc": null,
      "createdAtUtc": "2026-11-16T18:50:00Z"
    }
  ],
  "unreadCount": 1,
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```
- **Mark Single Read:** `POST /api/v1/notifications/{id}/read` -> `200 OK`
- **Mark All Read:** `POST /api/v1/notifications/read-all` -> `200 OK` (`{ "markedCount": 3 }`)

#### 7. System Diagnostics & Health Probes
- **Get System Info:** `GET /api/v1/system/info`
```json
{
  "application": "NexusDesk.Api",
  "environment": "Development",
  "framework": ".NET 9.0.22",
  "osVersion": "Microsoft Windows 10.0.26300",
  "processArchitecture": "X64",
  "loadedModules": [
    "Tenants",
    "Identity",
    "Ticketing",
    "Chat",
    "KnowledgeBase",
    "Automation",
    "Billing",
    "Analytics"
  ],
  "connectivity": {
    "overall": "Healthy",
    "postgresql": "Healthy",
    "redis": "Healthy"
  },
  "timestampUtc": "2026-11-16T19:55:00Z"
}
```
- **Liveness Probe:** `GET /health/live` -> `200 OK` (`{"status":"Healthy"}`)
- **Readiness Probe:** `GET /health/ready` -> `200 OK` (verifies PostgreSQL pgvector + Redis)

---

## 4. SignalR Real-Time Hubs Contract

Both hubs support authentication via standard query string parameter `?access_token=<jwt>` or `Authorization: Bearer <jwt>`.

### 4.1 Ticket Hub (`/hubs/tickets`)
- **Connection URL:** `http://localhost:56100/hubs/tickets?access_token=<jwt>`
- **Automatic Group Membership On Connection:**
  - `tenant:{tenantId}` (All users in tenant)
  - `tenant:{tenantId}:user:{userId}` (Direct user notifications)
  - `tenant:{tenantId}:staff` (Agents and Admins only)

#### Client -> Server Methods

| Method Name | Parameter Types | Caller | Description |
| :--- | :--- | :--- | :--- |
| `JoinTicketRoom` | `Guid ticketId` | Staff or Ticket Owner | Joins ticket room; triggers presence update if caller is staff |
| `LeaveTicketRoom` | `Guid ticketId` | Any | Leaves ticket room; removes presence |
| `SetTypingState` | `Guid ticketId, bool isTyping` | Staff | Broadcasts agent typing state to ticket staff room |

#### Server -> Client Events

##### 1. `TicketCreated`
```json
{
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "ticketNumber": "ND-10001",
  "title": "Cannot access billing portal from Chrome",
  "status": "New",
  "priority": "High",
  "customerId": "22222222-2222-2222-2222-222222222223",
  "assignedAgentId": null,
  "eventType": "Created",
  "occurredAtUtc": "2026-11-16T18:45:00Z"
}
```

##### 2. `TicketUpdated`
*(Same payload shape as `TicketCreated` with updated `status`, `assignedAgentId`, or `priority`)*.

##### 3. `TicketCommentAdded`
```json
{
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "commentId": "77889900-1122-3344-5566-778899aabbcc",
  "authorUserId": "22222222-2222-2222-2222-222222222222",
  "isCustomer": false,
  "type": "PublicReply",
  "body": "We have pushed a patch to resolve session token expiry.",
  "createdAtUtc": "2026-11-16T18:55:00Z"
}
```

##### 4. `AgentPresenceUpdated`
```json
{
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "activeAgents": [
    {
      "userId": "22222222-2222-2222-2222-222222222222",
      "displayName": "Acme Agent",
      "isTyping": true,
      "lastActivityUtc": "2026-11-16T18:56:00Z"
    }
  ],
  "updatedAtUtc": "2026-11-16T18:56:00Z"
}
```

##### 5. `SlaBreachAlert`
```json
{
  "ticketId": "652f5d1c-aed3-426d-aad5-48273c5140ad",
  "ticketNumber": "ND-10001",
  "priority": "High",
  "breachType": "FirstResponseDueUtc",
  "breachedAtUtc": "2026-11-16T19:45:00Z"
}
```

---

### 4.2 Live Chat Hub (`/hubs/chat`)
- **Connection URL:** `http://localhost:56100/hubs/chat?access_token=<jwt>`
- **Automatic Group Membership On Connection:**
  - `chat:tenant:{tenantId}:all`
  - `chat:tenant:{tenantId}:staff` (Staff members only)

#### Client -> Server Methods

| Method Name | Parameter Types | Caller | Description |
| :--- | :--- | :--- | :--- |
| `JoinChatSession` | `Guid sessionId` | Session Customer or Staff | Joins chat room group `chat:tenant:{tenantId}:session:{sessionId}` |
| `LeaveChatSession` | `Guid sessionId` | Any | Leaves chat room group |
| `SendTypingIndicator` | `Guid sessionId, bool isTyping` | Any | Broadcasts typing presence indicator to room participants |

#### Server -> Client Events

##### 1. `ChatMessageReceived`
```json
{
  "id": "8899aabb-ccdd-eeff-0011-223344556677",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "chatSessionId": "33445566-7788-99aa-bbcc-ddeeff001122",
  "senderUserId": "22222222-2222-2222-2222-222222222222",
  "senderRole": "Agent",
  "content": "Hello! How may I assist you today?",
  "sentAtUtc": "2026-11-16T19:12:00Z"
}
```

##### 2. `ChatSessionClaimed`
```json
{
  "id": "33445566-7788-99aa-bbcc-ddeeff001122",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "customerId": "22222222-2222-2222-2222-222222222223",
  "assignedAgentId": "22222222-2222-2222-2222-222222222222",
  "subject": "Pre-sales inquiry regarding Enterprise dedicated database",
  "status": "Active",
  "convertedTicketId": null,
  "createdAtUtc": "2026-11-16T19:10:00Z",
  "closedAtUtc": null
}
```

##### 3. `ChatSessionClosed`
```json
{
  "id": "33445566-7788-99aa-bbcc-ddeeff001122",
  "tenantId": "b7e43bc4-a5c7-4c8f-a470-7277ce9823f4",
  "status": "Closed",
  "convertedTicketId": null,
  "closedAtUtc": "2026-11-16T19:25:00Z"
}
```

##### 4. `ChatTypingIndicator`
```json
{
  "sessionId": "33445566-7788-99aa-bbcc-ddeeff001122",
  "userId": "22222222-2222-2222-2222-222222222222",
  "userName": "Acme Agent",
  "isTyping": true
}
```

---

## 5. GraphQL API Schema (`/graphql`)

Powered by HotChocolate. All operations require an authenticated JWT token and valid tenant context (`X-Tenant-Subdomain` header).

### 5.1 Query Operations

```graphql
type Query {
  # Retrieve tickets for the tenant with optional status filter and limit
  tickets(
    first: Int
    status: String
  ): [TicketGqlType!]!

  # Retrieve published knowledge base articles with optional search keyword
  kbArticles(
    search: String
  ): [KbArticleGqlType!]!

  # TenantAdmin only: subscription plan details and real-time usage meters
  billingOverview: BillingOverviewGqlType!

  # Agent & Admin: executive performance metrics across specified lookback window
  analyticsOverview(
    days: Int
  ): AnalyticsOverviewGqlType!

  # Shortcut for 30-day executive performance metrics
  analyticsSummary: AnalyticsOverviewGqlType!
}
```

---

### 5.2 Return Types & camelCase Fields

```graphql
type TicketGqlType {
  id: UUID!
  ticketNumber: String!
  title: String!
  description: String!
  status: String!
  priority: String!
  channel: String!
  customerId: UUID!
  assignedAgentId: UUID
  isSlaBreached: Boolean!
  firstResponseDueAtUtc: DateTime
  resolutionDueAtUtc: DateTime
  tags: [String!]!
  createdAtUtc: DateTime!
  updatedAtUtc: DateTime
}

type KbArticleGqlType {
  id: UUID!
  tenantId: UUID!
  categoryId: UUID!
  title: String!
  slug: String!
  contentMarkdown: String!
  isPublished: Boolean!
  createdAtUtc: DateTime!
  updatedAtUtc: DateTime
}

type BillingOverviewGqlType {
  planCode: String!
  planName: String!
  status: String!
  currentPeriodStartUtc: DateTime!
  currentPeriodEndUtc: DateTime!
  maxAgents: Int!
  maxMonthlyTickets: Int!
  maxMonthlyAiTokens: Long!
  monthlyPriceCents: Int!
  ticketsCreatedThisPeriod: Long!
  activeAgents: Long!
  aiTokensUsedThisPeriod: Long!
  cancelAtPeriodEnd: Boolean!
}

type AnalyticsOverviewGqlType {
  totalTickets: Int!
  openTickets: Int!
  resolvedTickets: Int!
  slaComplianceRate: Float!
  averageFirstResponseMinutes: Float!
  averageResolutionMinutes: Float!
  csatAverageScore: Float!
  activeChatSessions: Int!
  publishedKbArticles: Int!
}
```

---

### 5.3 Example GraphQL Queries & Payload Responses

#### 1. Query Tickets & Analytics Summary in a Single Request
- **Endpoint:** `POST /graphql`
- **Headers:** `Authorization: Bearer <token>`, `X-Tenant-Subdomain: acme`
- **Query Body:**
```graphql
query GetDashboardData {
  tickets(first: 5, status: "Open") {
    id
    ticketNumber
    title
    status
    priority
    assignedAgentId
    isSlaBreached
    tags
    createdAtUtc
  }
  analyticsSummary {
    totalTickets
    openTickets
    resolvedTickets
    slaComplianceRate
    csatAverageScore
    activeChatSessions
  }
}
```
- **Response JSON:**
```json
{
  "data": {
    "tickets": [
      {
        "id": "652f5d1c-aed3-426d-aad5-48273c5140ad",
        "ticketNumber": "ND-10001",
        "title": "Cannot access billing portal from Chrome",
        "status": "Open",
        "priority": "High",
        "assignedAgentId": "22222222-2222-2222-2222-222222222222",
        "isSlaBreached": false,
        "tags": ["billing", "auth", "chrome"],
        "createdAtUtc": "2026-11-16T18:45:00.000Z"
      }
    ],
    "analyticsSummary": {
      "totalTickets": 248,
      "openTickets": 18,
      "resolvedTickets": 230,
      "slaComplianceRate": 98.4,
      "csatAverageScore": 4.85,
      "activeChatSessions": 3
    }
  }
}
```

#### 2. Query Knowledge Base Articles
```graphql
query SearchKbArticles {
  kbArticles(search: "SSO") {
    id
    title
    slug
    contentMarkdown
    isPublished
    createdAtUtc
  }
}
```

---

## 6. Frontend Enums Reference Matrix

All enums are serialized as exact casing strings:

### `TicketStatus`
- `"New"`
- `"Open"`
- `"PendingCustomer"`
- `"PendingInternal"`
- `"Resolved"`
- `"Closed"`

### `TicketPriority`
- `"Low"`
- `"Medium"`
- `"High"`
- `"Urgent"`

### `TicketChannel`
- `"WebPortal"`
- `"Email"`
- `"Api"`
- `"LiveChat"`

### `CommentType`
- `"PublicReply"`
- `"InternalNote"`

### `ChatSessionStatus`
- `"Waiting"`
- `"Active"`
- `"Closed"`
- `"ConvertedToTicket"`

### `TenantPlanTier`
- `"Free"`
- `"Pro"`
- `"Enterprise"`

### `IsolationStrategy`
- `"SharedDatabase"`
- `"DedicatedDatabase"`

### `TenantStatus`
- `"Active"`
- `"Suspended"`

### `KbArticleStatus`
- `"Draft"`
- `"Published"`
- `"Archived"`
