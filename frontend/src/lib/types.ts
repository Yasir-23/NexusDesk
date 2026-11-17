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
