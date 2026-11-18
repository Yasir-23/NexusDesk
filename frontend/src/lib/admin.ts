import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import { useSessionScope, type SessionScope } from './queries';
import type {
  AuditLogQuery,
  AutomationRule,
  CreateAutomationRuleRequest,
  CreateWebhookRequest,
  DeadLetterWebhook,
  OutboxMessage,
  PagedAuditLogsResponse,
  SlaPolicy,
  UpsertSlaPolicyRequest,
  WebhookSubscription,
} from './types';

// Tenant-admin endpoints (`/api/v1/tenant-admin/*`, `/api/v1/audit-logs`).
// Every key ends with the session scope, like the M1/M2 keys.
export const adminKeys = {
  slaRoot: ['admin', 'sla'] as const,
  sla: (scope: SessionScope) => ['admin', 'sla', scope] as const,
  rulesRoot: ['admin', 'rules'] as const,
  rules: (scope: SessionScope) => ['admin', 'rules', scope] as const,
  webhooksRoot: ['admin', 'webhooks'] as const,
  webhooks: (scope: SessionScope) => ['admin', 'webhooks', 'list', scope] as const,
  outbox: (scope: SessionScope) => ['admin', 'webhooks', 'outbox', scope] as const,
  deadLetters: (scope: SessionScope) => ['admin', 'webhooks', 'dead-letters', scope] as const,
  audit: (q: AuditLogQuery, scope: SessionScope) => ['admin', 'audit', q, scope] as const,
};

const BASE = '/api/v1/tenant-admin';

// ---------- SLA policies ----------

export function useSlaPolicies() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: adminKeys.sla(scope),
    queryFn: ({ signal }) => api.get<SlaPolicy[]>(`${BASE}/sla-policies`, { signal }),
    enabled: Boolean(scope.tenant),
  });
}

/** `PUT /api/v1/tenant-admin/sla-policies` — insert or update the policy for one priority. */
export function useUpsertSlaPolicy() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (body: UpsertSlaPolicyRequest) => api.put<SlaPolicy>(`${BASE}/sla-policies`, body, { silent: true }),
    onSuccess: (policy) => {
      qc.setQueryData<SlaPolicy[]>(adminKeys.sla(scope), (list) =>
        list ? [...list.filter((p) => p.priority !== policy.priority), policy] : [policy],
      );
      void qc.invalidateQueries({ queryKey: adminKeys.slaRoot });
    },
  });
}

// ---------- Automation rules ----------

/** `GET /api/v1/tenant-admin/automation-rules` (exposed by the backend; not listed in the contract doc). */
export function useAutomationRules() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: adminKeys.rules(scope),
    queryFn: ({ signal }) => api.get<AutomationRule[]>(`${BASE}/automation-rules`, { signal }),
    enabled: Boolean(scope.tenant),
  });
}

export function useCreateAutomationRule() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (body: CreateAutomationRuleRequest) =>
      api.post<AutomationRule>(`${BASE}/automation-rules`, body, { silent: true }),
    onSuccess: (rule) => {
      qc.setQueryData<AutomationRule[]>(adminKeys.rules(scope), (list) => [rule, ...(list ?? []).filter((r) => r.id !== rule.id)]);
      void qc.invalidateQueries({ queryKey: adminKeys.rulesRoot });
    },
  });
}

/** `PATCH .../automation-rules/{id}/toggle` with an explicit `isEnabled`, applied optimistically. */
export function useToggleAutomationRule() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  const key = adminKeys.rules(scope);
  return useMutation({
    mutationFn: ({ id, isEnabled }: { id: string; isEnabled: boolean }) =>
      api.patch<AutomationRule>(`${BASE}/automation-rules/${encodeURIComponent(id)}/toggle`, { isEnabled }),
    onMutate: async ({ id, isEnabled }) => {
      await qc.cancelQueries({ queryKey: key });
      const previous = qc.getQueryData<AutomationRule[]>(key);
      qc.setQueryData<AutomationRule[]>(key, (list) => list?.map((r) => (r.id === id ? { ...r, isEnabled } : r)));
      return { previous };
    },
    onError: (_err, _vars, ctx) => {
      if (ctx?.previous) qc.setQueryData(key, ctx.previous);
    },
    onSuccess: (rule) => {
      qc.setQueryData<AutomationRule[]>(key, (list) => list?.map((r) => (r.id === rule.id ? rule : r)));
    },
  });
}

// ---------- Webhooks, outbox, dead letters ----------

export function useWebhooks() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: adminKeys.webhooks(scope),
    queryFn: ({ signal }) => api.get<WebhookSubscription[]>(`${BASE}/webhooks`, { signal }),
    enabled: Boolean(scope.tenant),
  });
}

export function useCreateWebhook() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateWebhookRequest) => api.post<WebhookSubscription>(`${BASE}/webhooks`, body, { silent: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: adminKeys.webhooksRoot }),
  });
}

/** Outbox + DLQ are diagnostic views of a background dispatcher, so they poll. */
export function useOutbox() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: adminKeys.outbox(scope),
    queryFn: ({ signal }) => api.get<OutboxMessage[]>(`${BASE}/webhooks/outbox`, { signal }),
    enabled: Boolean(scope.tenant),
    refetchInterval: 10_000,
  });
}

export function useDeadLetters() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: adminKeys.deadLetters(scope),
    queryFn: ({ signal }) => api.get<DeadLetterWebhook[]>(`${BASE}/webhooks/dead-letters`, { signal }),
    enabled: Boolean(scope.tenant),
    refetchInterval: 10_000,
  });
}

// ---------- Audit log ----------

/**
 * `GET /api/v1/audit-logs?actorEmail=&resourceType=&page=&pageSize=`. The backend filters by
 * `resourceType` server-side but (as of v1.0.0) ignores `actorEmail`, so the page applies the
 * actor filter to the returned rows as well.
 */
export function useAuditLogs(q: AuditLogQuery) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: adminKeys.audit(q, scope),
    queryFn: ({ signal }) =>
      api.get<PagedAuditLogsResponse>('/api/v1/audit-logs', {
        query: { actorEmail: q.actorEmail, resourceType: q.resourceType, page: q.page, pageSize: q.pageSize },
        signal,
      }),
    enabled: Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}
