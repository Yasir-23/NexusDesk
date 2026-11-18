import { useQuery } from '@tanstack/react-query';
import { api, apiDownload, saveBlob } from './api';
import { useSessionScope, type SessionScope } from './queries';
import type { AgentPerformance, AnalyticsOverview, DailyTicketTrend } from './types';

export const LOOKBACK_WINDOWS = [7, 30, 90] as const;
export type LookbackDays = (typeof LOOKBACK_WINDOWS)[number];

export const analyticsKeys = {
  root: ['analytics'] as const,
  overview: (days: number, scope: SessionScope) => ['analytics', 'overview', days, scope] as const,
  trends: (days: number, scope: SessionScope) => ['analytics', 'trends', days, scope] as const,
  agents: (days: number, scope: SessionScope) => ['analytics', 'agents', days, scope] as const,
};

export function useAnalyticsOverview(days: number) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: analyticsKeys.overview(days, scope),
    queryFn: ({ signal }) => api.get<AnalyticsOverview>('/api/v1/analytics/overview', { query: { days }, signal }),
    enabled: Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

export function useTicketTrends(days: number) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: analyticsKeys.trends(days, scope),
    queryFn: ({ signal }) => api.get<DailyTicketTrend[]>('/api/v1/analytics/tickets/trends', { query: { days }, signal }),
    enabled: Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

export function useAgentPerformance(days: number) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: analyticsKeys.agents(days, scope),
    queryFn: ({ signal }) => api.get<AgentPerformance[]>('/api/v1/analytics/agents/performance', { query: { days }, signal }),
    enabled: Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

/** Authenticated blob download of `GET /api/v1/analytics/export/tickets.csv?days=`. */
export async function exportTicketsCsv(days: number) {
  const { blob, fileName } = await apiDownload(`/api/v1/analytics/export/tickets.csv?days=${days}`);
  const name = fileName ?? `nexusdesk-tickets-${days}d.csv`;
  saveBlob(blob, name);
  return { fileName: name, size: blob.size };
}
