import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, probe } from './api';
import { useAuthStore } from '../stores/auth';
import type {
  LoginResponse,
  MeResponse,
  PagedTenantsResponse,
  ProvisionTenantRequest,
  ProvisionTenantResponse,
  RegisterCustomerRequest,
  RegisterCustomerResponse,
  SystemInfoResponse,
  TenantCurrentResponse,
  TenantDetailsResponse,
  UpdateTenantStatusRequest,
  UpdateTenantStatusResponse,
} from './types';

/**
 * Identity + tenant a cached response belongs to. It is the last element of every query key,
 * so a login, logout, quick account switch or SuperAdmin tenant switch always lands on fresh
 * keys (and therefore fresh requests) instead of the previous identity's cache entries.
 */
export interface SessionScope {
  user: string | null;
  tenant: string | null;
}

export function sessionScope(state: { user: { id: string } | null; tenantSubdomain: string | null }): SessionScope {
  return { user: state.user?.id ?? null, tenant: state.tenantSubdomain };
}

export function useSessionScope(): SessionScope {
  const user = useAuthStore((s) => s.user?.id ?? null);
  const tenant = useAuthStore((s) => s.tenantSubdomain);
  return { user, tenant };
}

export function isSameScope(a: SessionScope, b: SessionScope) {
  return a.user === b.user && a.tenant === b.tenant;
}

/** Reads the scope stamped on a query key by {@link queryKeys}; `undefined` for unscoped keys. */
export function scopeOfKey(queryKey: readonly unknown[]): SessionScope | undefined {
  const last = queryKey.at(-1);
  return last && typeof last === 'object' && 'user' in last && 'tenant' in last ? (last as SessionScope) : undefined;
}

// Prefixes (first elements) stay stable so `invalidateQueries({ queryKey: [...prefix] })` still matches.
export const queryKeys = {
  me: (scope: SessionScope) => ['auth', 'me', scope] as const,
  tenantCurrent: (subdomain: string | null, scope: SessionScope) => ['tenant', 'current', subdomain, scope] as const,
  tenantCurrentRoot: ['tenant', 'current'] as const,
  platformTenants: (page: number, pageSize: number, scope: SessionScope) => ['platform', 'tenants', page, pageSize, scope] as const,
  platformTenantsRoot: ['platform', 'tenants'] as const,
  platformTenant: (id: string, scope: SessionScope) => ['platform', 'tenant', id, scope] as const,
  platformTenantRoot: (id: string) => ['platform', 'tenant', id] as const,
  systemInfo: (scope: SessionScope) => ['system', 'info', scope] as const,
  health: (path: string, scope: SessionScope) => ['health', path, scope] as const,
};

// ---------- Auth ----------

/** `POST /api/v1/auth/login`. Tenant users must send `X-Tenant-Subdomain`. */
export async function login(email: string, password: string, subdomain: string | null): Promise<LoginResponse> {
  const response = await api.post<LoginResponse>(
    '/api/v1/auth/login',
    { email: email.trim(), password },
    { tenant: subdomain, auth: false, silent: true },
  );
  useAuthStore.getState().setSession(response, subdomain);
  return response;
}

export function registerCustomer(subdomain: string, body: RegisterCustomerRequest) {
  return api.post<RegisterCustomerResponse>('/api/v1/users/customers/register', body, {
    tenant: subdomain,
    auth: false,
    silent: true,
  });
}

export function useMe() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: queryKeys.me(scope),
    queryFn: ({ signal }) => api.get<MeResponse>('/api/v1/auth/me', { signal }),
    enabled: Boolean(scope.user),
  });
}

// ---------- Tenant context ----------

/** `GET /api/v1/tenant/current` for the active (or given) tenant subdomain. */
export function useTenantCurrent(subdomainOverride?: string | null) {
  const scope = useSessionScope();
  const subdomain = subdomainOverride === undefined ? scope.tenant : subdomainOverride;
  return useQuery({
    queryKey: queryKeys.tenantCurrent(subdomain, scope),
    queryFn: ({ signal }) =>
      api.get<TenantCurrentResponse>('/api/v1/tenant/current', { tenant: subdomain, signal, silent: true }),
    enabled: Boolean(subdomain),
    staleTime: 30_000,
  });
}

// ---------- Platform (SuperAdmin) ----------

export function usePlatformTenants(page: number, pageSize: number) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: queryKeys.platformTenants(page, pageSize, scope),
    queryFn: ({ signal }) =>
      api.get<PagedTenantsResponse>('/api/v1/platform/tenants', { query: { page, pageSize }, tenant: null, signal }),
    placeholderData: (prev) => prev,
  });
}

export function usePlatformTenant(tenantId: string | null) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: queryKeys.platformTenant(tenantId ?? '', scope),
    queryFn: ({ signal }) =>
      api.get<TenantDetailsResponse>(`/api/v1/platform/tenants/${encodeURIComponent(tenantId!)}`, { tenant: null, signal }),
    enabled: Boolean(tenantId),
  });
}

export function useProvisionTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ProvisionTenantRequest) =>
      api.post<ProvisionTenantResponse>('/api/v1/platform/tenants', body, { tenant: null, silent: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.platformTenantsRoot }),
  });
}

export function useUpdateTenantStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ tenantId, ...body }: UpdateTenantStatusRequest & { tenantId: string }) =>
      api.patch<UpdateTenantStatusResponse>(`/api/v1/platform/tenants/${encodeURIComponent(tenantId)}/status`, body, {
        tenant: null,
      }),
    onSuccess: (result, vars) => {
      void qc.invalidateQueries({ queryKey: queryKeys.platformTenantsRoot });
      void qc.invalidateQueries({ queryKey: queryKeys.platformTenantRoot(vars.tenantId) });
      void qc.invalidateQueries({ queryKey: [...queryKeys.tenantCurrentRoot, result.subdomain] });
    },
  });
}

export function useSystemInfo() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: queryKeys.systemInfo(scope),
    queryFn: ({ signal }) => api.get<SystemInfoResponse>('/api/v1/system/info', { tenant: null, signal }),
    refetchInterval: 15_000,
  });
}

export function useHealthProbe(path: '/health/ready' | '/health/live') {
  const scope = useSessionScope();
  return useQuery({
    queryKey: queryKeys.health(path, scope),
    queryFn: ({ signal }) => probe(path, signal),
    refetchInterval: 15_000,
  });
}
