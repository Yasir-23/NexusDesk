# Product Requirements Document (PRD)
**Project:** NexusDesk — Multi-Tenant Cloud-Native SaaS Helpdesk Platform  
**Version:** 1.0.0  
**Architecture Stack:** .NET 9 LTS, PostgreSQL (Marten + EF Core 9 + pgvector), Redis, React 19 + TypeScript + Tailwind CSS  

---

## 1. Product Vision & Scope
NexusDesk is a multi-tenant B2B customer support and helpdesk platform built to host 500+ isolated companies (tenants). It provides omnichannel ticketing (event-sourced), real-time customer-to-agent live chat, a versioned knowledge base, automated SLA enforcement, external webhooks, and tenant-isolated Retrieval-Augmented Generation (RAG) AI features.

---

## 2. User Roles & Access Matrix

| Role | Plane | Core Capabilities |
| :--- | :--- | :--- |
| **Platform Super Admin** | Global Plane | Onboard/suspend/reactivate tenants, manage subscription plans, view cross-tenant usage metering, inspect global DLQs and system health. |
| **Tenant Admin** | Tenant Plane | Manage company settings, invite/deactivate agents within plan limits, configure SLA rules, configure automation rules and webhooks, view tenant analytics & billing invoices. |
| **Agent** | Tenant Plane | Manage assigned tickets, collaborate via internal notes, respond to live customer chats, trigger AI reply suggestions & ticket summaries, author KB articles. |
| **Customer** | Tenant Plane | Submit tickets with attachments, track ticket status, chat live with agents, search published KB articles, trigger GDPR personal data anonymization. |

---

## 3. Subscription Plans & Quotas

| Tier | Tenancy Model | Max Agents | Max Tickets / Mo | Storage Quota | Rate Limit | AI & Webhooks |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Free** | Shared DB (`TenantId` + RLS) | 2 | 100 | 500 MB | 60 req/min | Basic Categorization |
| **Pro** | Shared DB (`TenantId` + RLS) | 15 | 2,500 | 25 GB | 600 req/min | Full RAG + Webhooks |
| **Enterprise** | Dedicated DB per Tenant | Unlimited | Unlimited | 1 TB | 5,000 req/min | Full RAG + Webhooks + Custom SLA |

---

## 4. Core Functional Requirements

### 4.1 Tenant Onboarding & Lifecycle
* Self-service registration provisioning a live subdomain (`{subdomain}.nexusdesk.local` / `{subdomain}.nexusdesk.com`).
* Automatic database provisioning (RLS registration for Free/Pro; isolated database creation and migration for Enterprise).
* Instant tenant suspension capability blocking all authentication and API mutations for that tenant's users within 5 seconds.

### 4.2 Immutable Event-Sourced Ticketing
* Every ticket state mutation is persisted exclusively as an immutable domain event (`TicketCreated`, `TicketAssigned`, `TicketPriorityChanged`, `TicketReplyAdded`, `TicketAttachmentScanned`, `TicketSlaBreached`, `TicketResolved`, `TicketClosed`).
* No UPDATE or DELETE statements are permitted on the event stream.
* Read-optimized CQRS projections can be dropped and deterministically rebuilt from the event history at any time.
* Attachments uploaded to Blob Storage undergo magic-byte validation and simulated antivirus scanning before becoming downloadable.

### 4.3 SLA Engine & Automation Rules
* Tenant Admins define response and resolution SLA targets per priority (`Low`, `Medium`, `High`, `Urgent`).
* Background durable timers evaluate active tickets and automatically trigger `TicketSlaBreached` events, escalate priority, notify managers, and dispatch external webhooks when breached.

### 4.4 Real-Time Chat (SignalR + Redis Backplane)
* Sub-second bidirectional messaging between Customers and Agents.
* Real-time typing indicators, online/offline presence tracking, and automatic fallback to offline ticket creation when no agents are online.

### 4.5 Knowledge Base & Tenant-Scoped AI
* Versioned Markdown articles organized by category with `Draft`, `Published`, and `Archived` states.
* Published articles are chunked and embedded into `pgvector` strictly partitioned by `TenantId`.
* AI pipeline provides:
  1. Auto-categorization and priority recommendation on ticket creation.
  2. RAG-based suggested replies referencing only the active tenant's published KB articles.
  3. One-click multi-message ticket thread summarization.
  4. Prompt injection sanitization rejecting cross-tenant extraction attempts.

### 4.6 GDPR Compliance & Data Protection
* Right to erasure workflow (`POST /api/v1/privacy/forget-me`) replaces user PII (`Email`, `FullName`, `Phone`, `IpAddress`) across relational tables and event payloads with cryptographic redaction tokens while preserving ticket audit structures.