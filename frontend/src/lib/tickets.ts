import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import { ApiProblemError, api, apiDownload, saveBlob } from './api';
import { useSessionScope, type SessionScope } from './queries';
import type {
  AddCommentRequest,
  AiInstructionsRequest,
  AssignTicketRequest,
  CreateTicketRequest,
  PagedTicketsResponse,
  SuggestReplyResponse,
  SummarizeTicketResponse,
  TenantUser,
  TicketAttachment,
  TicketComment,
  TicketDetails,
  TicketListQuery,
  UpdateTicketPriorityRequest,
  UpdateTicketStatusRequest,
} from './types';

// Every key ends with the session scope (user + tenant), so switching account or tenant
// always lands on fresh keys and SessionScopeSync evicts the previous identity's data.
export const ticketKeys = {
  root: ['tickets'] as const,
  listRoot: ['tickets', 'list'] as const,
  list: (q: TicketListQuery, scope: SessionScope) => ['tickets', 'list', q, scope] as const,
  detailRoot: (id: string) => ['tickets', 'detail', id] as const,
  detail: (id: string, scope: SessionScope) => ['tickets', 'detail', id, scope] as const,
  attachments: (id: string, scope: SessionScope) => ['tickets', 'attachments', id, scope] as const,
  users: (scope: SessionScope) => ['users', 'list', scope] as const,
};

const enc = encodeURIComponent;

// ---------- Users (agent names, customer picker) ----------

/** `GET /api/v1/users` — staff only. */
export function useTenantUsers(enabled = true) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: ticketKeys.users(scope),
    queryFn: ({ signal }) => api.get<TenantUser[]>('/api/v1/users', { signal, silent: true }),
    enabled: enabled && Boolean(scope.user && scope.tenant),
    staleTime: 60_000,
  });
}

/** id → display name lookup built from the tenant user list. */
export function useUserNames(enabled = true) {
  const { data } = useTenantUsers(enabled);
  return useMemo(() => new Map<string, TenantUser>((data ?? []).map((u) => [u.id, u])), [data]);
}

// ---------- Queue & details ----------

export function useTickets(q: TicketListQuery) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: ticketKeys.list(q, scope),
    queryFn: ({ signal }) =>
      api.get<PagedTicketsResponse>('/api/v1/tickets', {
        query: {
          status: q.status,
          priority: q.priority,
          assignedAgentId: q.assignedAgentId,
          customerId: q.customerId,
          page: q.page,
          pageSize: q.pageSize,
        },
        signal,
      }),
    enabled: Boolean(scope.tenant),
    placeholderData: (prev) => prev,
  });
}

export function useTicket(id: string | undefined) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: ticketKeys.detail(id ?? '', scope),
    queryFn: ({ signal }) => api.get<TicketDetails>(`/api/v1/tickets/${enc(id!)}`, { signal }),
    enabled: Boolean(id && scope.tenant),
  });
}

export function useCreateTicket() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateTicketRequest) => api.post<TicketDetails>('/api/v1/tickets', body, { silent: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ticketKeys.listRoot }),
  });
}

/** Adds a comment to the cached ticket unless it is already there (REST + SignalR both deliver it). */
export function appendComment(qc: QueryClient, key: readonly unknown[], comment: TicketComment) {
  qc.setQueryData<TicketDetails>(key, (t) =>
    t && !t.comments.some((c) => c.id === comment.id) ? { ...t, comments: [...t.comments, comment] } : t,
  );
}

/** Shared plumbing for the optimistic-concurrency PATCH endpoints (assign / status / priority). */
function useTicketPatch<TBody>(id: string, segment: 'assign' | 'status' | 'priority') {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (body: TBody) => api.patch<TicketDetails>(`/api/v1/tickets/${enc(id)}/${segment}`, body),
    onSuccess: (ticket) => {
      qc.setQueryData(ticketKeys.detail(id, scope), ticket);
      void qc.invalidateQueries({ queryKey: ticketKeys.listRoot });
    },
    onError: (err) => {
      // 409 = stale rowVersion: someone else changed the ticket. Reload the latest copy.
      if (err instanceof ApiProblemError && err.status === 409) void qc.invalidateQueries({ queryKey: ticketKeys.detailRoot(id) });
    },
  });
}

export const useAssignTicket = (id: string) => useTicketPatch<AssignTicketRequest>(id, 'assign');
export const useUpdateTicketStatus = (id: string) => useTicketPatch<UpdateTicketStatusRequest>(id, 'status');
export const useUpdateTicketPriority = (id: string) => useTicketPatch<UpdateTicketPriorityRequest>(id, 'priority');

export function useAddComment(id: string) {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (body: AddCommentRequest) => api.post<TicketComment>(`/api/v1/tickets/${enc(id)}/comments`, body),
    onSuccess: (comment) => {
      appendComment(qc, ticketKeys.detail(id, scope), comment);
      // First reply changes status / SLA fields server-side.
      void qc.invalidateQueries({ queryKey: ticketKeys.detailRoot(id) });
      void qc.invalidateQueries({ queryKey: ticketKeys.listRoot });
    },
  });
}

// ---------- Attachments ----------

export const ATTACHMENT_EXTENSIONS = ['.png', '.jpg', '.jpeg', '.pdf', '.txt', '.log', '.csv', '.zip'];
export const ATTACHMENT_MAX_BYTES = 10 * 1024 * 1024;

/** Client-side pre-check mirroring the backend rules; returns an error message or `null`. */
export function validateAttachment(file: File): string | null {
  const ext = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : '';
  if (!ATTACHMENT_EXTENSIONS.includes(ext)) return `"${file.name}" is not an allowed type (${ATTACHMENT_EXTENSIONS.join(', ')}).`;
  if (file.size > ATTACHMENT_MAX_BYTES) return `"${file.name}" exceeds the 10 MB limit.`;
  return null;
}

export function useAttachments(ticketId: string) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: ticketKeys.attachments(ticketId, scope),
    queryFn: ({ signal }) => api.get<TicketAttachment[]>(`/api/v1/tickets/${enc(ticketId)}/attachments`, { signal }),
    enabled: Boolean(scope.tenant),
  });
}

export function useUploadAttachment(ticketId: string) {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return api.post<TicketAttachment>(`/api/v1/tickets/${enc(ticketId)}/attachments`, form);
    },
    onSuccess: (attachment) => {
      qc.setQueryData<TicketAttachment[]>(ticketKeys.attachments(ticketId, scope), (list) =>
        list && !list.some((a) => a.id === attachment.id) ? [...list, attachment] : list,
      );
      void qc.invalidateQueries({ queryKey: ['tickets', 'attachments', ticketId] });
    },
  });
}

export async function downloadAttachment(attachment: TicketAttachment) {
  const { blob, fileName } = await apiDownload(
    `/api/v1/tickets/${enc(attachment.ticketId)}/attachments/${enc(attachment.id)}/download`,
  );
  saveBlob(blob, fileName ?? attachment.fileName);
}

// ---------- AI Ticket Assist ----------

function aiBody(customInstructions: string): AiInstructionsRequest {
  const trimmed = customInstructions.trim();
  return trimmed ? { customInstructions: trimmed } : {};
}

/** Errors render inline in the AI card (incl. `PROMPT_INJECTION_DETECTED`), so no toast. */
export function useSuggestReply(ticketId: string) {
  return useMutation({
    mutationFn: (customInstructions: string) =>
      api.post<SuggestReplyResponse>(`/api/v1/ai/tickets/${enc(ticketId)}/suggest-reply`, aiBody(customInstructions), {
        silent: true,
      }),
  });
}

export function useSummarizeTicket(ticketId: string) {
  return useMutation({
    mutationFn: (customInstructions: string) =>
      api.post<SummarizeTicketResponse>(`/api/v1/ai/tickets/${enc(ticketId)}/summarize`, aiBody(customInstructions), {
        silent: true,
      }),
  });
}
