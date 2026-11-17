import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from './api';
import { useSessionScope, type SessionScope } from './queries';
import type { ChatMessage, ChatSession, ChatSessionClosed, ChatSessionSummary } from './types';

export const chatKeys = {
  root: ['chat'] as const,
  sessionsRoot: ['chat', 'sessions'] as const,
  sessions: (scope: SessionScope) => ['chat', 'sessions', scope] as const,
  sessionRoot: (id: string) => ['chat', 'session', id] as const,
  session: (id: string, scope: SessionScope) => ['chat', 'session', id, scope] as const,
};

const enc = encodeURIComponent;

/** `GET /api/v1/chat/sessions` — staff see the whole tenant, customers only their own. */
export function useChatSessions() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: chatKeys.sessions(scope),
    queryFn: ({ signal }) => api.get<ChatSessionSummary[]>('/api/v1/chat/sessions', { signal }),
    enabled: Boolean(scope.tenant),
    // Hub events invalidate this list; the interval is only a safety net while offline.
    refetchInterval: 30_000,
  });
}

export function useChatSession(id: string | null) {
  const scope = useSessionScope();
  return useQuery({
    queryKey: chatKeys.session(id ?? '', scope),
    queryFn: ({ signal }) => api.get<ChatSession>(`/api/v1/chat/sessions/${enc(id!)}`, { signal }),
    enabled: Boolean(id && scope.tenant),
  });
}

/** Adds a message to the cached session once (REST response and hub event both deliver it). */
export function appendChatMessage(qc: QueryClient, key: readonly unknown[], message: ChatMessage) {
  qc.setQueryData<ChatSession>(key, (s) => {
    if (!s) return s;
    const messages = s.messages ?? [];
    return messages.some((m) => m.id === message.id) ? s : { ...s, messages: [...messages, message] };
  });
}

export function useStartChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (subject: string) => api.post<ChatSession>('/api/v1/chat/sessions', { subject }),
    onSuccess: () => qc.invalidateQueries({ queryKey: chatKeys.sessionsRoot }),
  });
}

export function useClaimChat() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (id: string) => api.post<ChatSession>(`/api/v1/chat/sessions/${enc(id)}/claim`),
    onSuccess: (session) => {
      qc.setQueryData<ChatSession>(chatKeys.session(session.id, scope), (prev) => ({
        ...session,
        messages: session.messages ?? prev?.messages ?? [],
      }));
      void qc.invalidateQueries({ queryKey: chatKeys.sessionsRoot });
    },
  });
}

export function useSendChatMessage(id: string) {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: (content: string) => api.post<ChatMessage>(`/api/v1/chat/sessions/${enc(id)}/messages`, { content }),
    onSuccess: (message) => appendChatMessage(qc, chatKeys.session(id, scope), message),
  });
}

export function useCloseChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, convertToTicket }: { id: string; convertToTicket: boolean }) =>
      api.post<ChatSessionClosed>(`/api/v1/chat/sessions/${enc(id)}/close`, { convertToTicket }),
    onSuccess: (closed) => {
      void qc.invalidateQueries({ queryKey: chatKeys.sessionRoot(closed.id) });
      void qc.invalidateQueries({ queryKey: chatKeys.sessionsRoot });
      if (closed.convertedTicketId) void qc.invalidateQueries({ queryKey: ['tickets', 'list'] });
    },
  });
}
