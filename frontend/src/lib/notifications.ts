import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import { useSessionScope, type SessionScope } from './queries';
import type { ForgetMeResponse, MarkAllNotificationsReadResponse, PagedNotificationsResponse, UserNotification } from './types';

export const NOTIFICATIONS_PAGE_SIZE = 10;
export const NOTIFICATIONS_POLL_MS = 30_000;

export const notificationKeys = {
  root: ['notifications'] as const,
  list: (scope: SessionScope) => ['notifications', 'list', scope] as const,
};

/** `GET /api/v1/notifications?unreadOnly=false&page=1&pageSize=10`, polled for the bell badge. */
export function useNotifications() {
  const scope = useSessionScope();
  return useQuery({
    queryKey: notificationKeys.list(scope),
    queryFn: ({ signal }) =>
      api.get<PagedNotificationsResponse>('/api/v1/notifications', {
        query: { unreadOnly: false, page: 1, pageSize: NOTIFICATIONS_PAGE_SIZE },
        signal,
        silent: true,
      }),
    // SuperAdmin has no tenant inbox (the endpoint needs an authenticated tenant user).
    enabled: Boolean(scope.user && scope.tenant),
    refetchInterval: NOTIFICATIONS_POLL_MS,
  });
}

export function useMarkNotificationRead() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  const key = notificationKeys.list(scope);
  return useMutation({
    mutationFn: (id: string) => api.post<UserNotification>(`/api/v1/notifications/${encodeURIComponent(id)}/read`),
    onMutate: (id) => {
      const previous = qc.getQueryData<PagedNotificationsResponse>(key);
      qc.setQueryData<PagedNotificationsResponse>(key, (page) => {
        if (!page) return page;
        const target = page.items.find((n) => n.id === id);
        if (!target || target.isRead) return page;
        return {
          ...page,
          unreadCount: Math.max(0, page.unreadCount - 1),
          items: page.items.map((n) => (n.id === id ? { ...n, isRead: true, readAtUtc: new Date().toISOString() } : n)),
        };
      });
      return { previous };
    },
    onError: (_e, _id, ctx) => ctx?.previous && qc.setQueryData(key, ctx.previous),
    onSettled: () => qc.invalidateQueries({ queryKey: notificationKeys.root }),
  });
}

export function useMarkAllNotificationsRead() {
  const qc = useQueryClient();
  const scope = useSessionScope();
  return useMutation({
    mutationFn: () => api.post<MarkAllNotificationsReadResponse>('/api/v1/notifications/read-all'),
    onSuccess: () => {
      const now = new Date().toISOString();
      qc.setQueryData<PagedNotificationsResponse>(notificationKeys.list(scope), (page) =>
        page && { ...page, unreadCount: 0, items: page.items.map((n) => (n.isRead ? n : { ...n, isRead: true, readAtUtc: now })) },
      );
      void qc.invalidateQueries({ queryKey: notificationKeys.root });
    },
  });
}

/** `POST /api/v1/privacy/forget-me` — irreversible GDPR self-erasure. */
export function useForgetMe() {
  return useMutation({
    mutationFn: () => api.post<ForgetMeResponse>('/api/v1/privacy/forget-me', undefined, { silent: true }),
  });
}
