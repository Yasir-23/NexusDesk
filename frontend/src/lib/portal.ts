import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import { appendComment } from './tickets';
import { useSessionScope, type SessionScope } from './queries';
import type {
  PagedTicketsResponse,
  PortalCreateTicketRequest,
  PortalTicketListQuery,
  TicketComment,
  TicketDetails,
} from './types';

// Customer self-service (`/api/v1/portal`). Kept under the `tickets` prefix so the realtime
// bridge's `TicketCreated` / `TicketUpdated` invalidations refresh the portal too.
export const portalKeys = {
  listRoot: ['tickets', 'portal', 'list'] as const,
  list: (q: PortalTicketListQuery, scope: SessionScope) => ['tickets', 'portal', 'list', q, scope] as const,
  detailRoot: (id: string) => ['tickets', 'portal', 'detail', id] as const,
  detail: (id: string, scope: SessionScope) => ['tickets', 'portal', 'detail', id, scope] as const,
};

const enc = encodeURIComponent;

export function usePortalTickets(q: PortalTicketListQuery) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: portalKeys.list(q, scope),
    queryFn: ({ signal }) =>
      api.get<PagedTicketsResponse>('/api/v1/portal/tickets', {
        query: { status: q.status, page: q.page, pageSize: q.pageSize },
        signal,
      }),
    enabled: Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

/** Server-side scrubbed: no `InternalNote` comments and no `activityLogs`. */
export function usePortalTicket(id: string | undefined) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: portalKeys.detail(id ?? '', scope),
    queryFn: ({ signal }) => api.get<TicketDetails>(`/api/v1/portal/tickets/${enc(id!)}`, { signal, silent: true }),
    enabled: Boolean(id && scope.tenant),
  });
}

export function useCreatePortalTicket() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: PortalCreateTicketRequest) => api.post<TicketDetails>('/api/v1/portal/tickets', body, { silent: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: portalKeys.listRoot }),
  });
}

export function usePortalReply(id: string) {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (message: string) => api.post<TicketComment>(`/api/v1/portal/tickets/${enc(id)}/replies`, { message }),
    onSuccess: (comment) => {
      appendComment(qc, portalKeys.detail(id, scope), comment);
      void qc.invalidateQueries({ queryKey: portalKeys.detailRoot(id) });
      void qc.invalidateQueries({ queryKey: portalKeys.listRoot });
    },
  });
}
