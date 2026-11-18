import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import { queryKeys, useSessionScope, type SessionScope } from './queries';
import { ticketKeys } from './tickets';
import type {
  ChangePlanRequest,
  CreateAgentRequest,
  CreateAgentResponse,
  SubscriptionPlan,
  TenantSubscriptionDetails,
} from './types';

export const billingKeys = {
  root: ['billing'] as const,
  plans: (scope: SessionScope) => ['billing', 'plans', scope] as const,
  subscription: (scope: SessionScope) => ['billing', 'subscription', scope] as const,
};

/** `GET /api/v1/billing/plans` — public catalog. */
export function useBillingPlans() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: billingKeys.plans(scope),
    queryFn: ({ signal }) => api.get<SubscriptionPlan[]>('/api/v1/billing/plans', { signal }),
    staleTime: 5 * 60_000,
  });
}

/** `GET /api/v1/billing/subscription` — plan, billing cycle and usage meters. */
export function useSubscription() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: billingKeys.subscription(scope),
    queryFn: ({ signal }) => api.get<TenantSubscriptionDetails>('/api/v1/billing/subscription', { signal }),
    enabled: Boolean(scope.tenant),
  });
}

export function useChangePlan() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (body: ChangePlanRequest) =>
      api.post<TenantSubscriptionDetails>('/api/v1/billing/subscription/change-plan', body, { silent: true }),
    onSuccess: (details) => {
      qc.setQueryData(billingKeys.subscription(scope), details);
      void qc.invalidateQueries({ queryKey: billingKeys.root });
      // The header badge reads the plan tier from /tenant/current.
      void qc.invalidateQueries({ queryKey: queryKeys.tenantCurrentRoot });
    },
  });
}

/** `POST /api/v1/users/agents`. Errors (incl. 422 `PLAN_AGENT_LIMIT_EXCEEDED`) render inline. */
export function useCreateAgent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAgentRequest) => api.post<CreateAgentResponse>('/api/v1/users/agents', body, { silent: true }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ticketKeys.usersRoot });
      void qc.invalidateQueries({ queryKey: billingKeys.root });
    },
  });
}

export const PLAN_AGENT_LIMIT_EXCEEDED = 'PLAN_AGENT_LIMIT_EXCEEDED';

export const USAGE_LABELS: Record<string, string> = {
  active_agents: 'Active agents',
  tickets_created: 'Tickets this period',
  ai_tokens_used: 'AI tokens this period',
  webhook_deliveries: 'Webhook deliveries this period',
};

export function formatPrice(cents: number) {
  return cents === 0 ? '$0' : `$${(cents / 100).toLocaleString('en-US', { maximumFractionDigits: 2 })}`;
}
