import { Bell, CheckCheck } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { Spinner } from '../ui';
import { Popover } from '../ui/Popover';
import { cn } from '../../lib/cn';
import { formatRelative } from '../../lib/format';
import { useMarkAllNotificationsRead, useMarkNotificationRead, useNotifications } from '../../lib/notifications';
import { useAuthStore } from '../../stores/auth';

/** Top-bar inbox for `GET /api/v1/notifications` (polled) with mark-read / mark-all-read. */
export function NotificationBell() {
  const hasTenant = useAuthStore((s) => Boolean(s.tenantSubdomain && s.user && s.user.role !== 'SuperAdmin'));
  const isCustomer = useAuthStore((s) => s.user?.role === 'Customer');
  const { data, isLoading } = useNotifications();
  const markOne = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const navigate = useNavigate();
  if (!hasTenant) return null;

  const unread = data?.unreadCount ?? 0;
  const badge = unread > 99 ? '99+' : String(unread);

  return (
    <Popover
      className="w-96 max-w-[calc(100vw-2rem)]"
      trigger={({ toggle, open }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label={unread ? `Notifications, ${unread} unread` : 'Notifications'}
          className="relative rounded-lg p-2 text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100"
        >
          <Bell className="h-4 w-4" />
          {unread > 0 && (
            <span
              data-testid="notification-badge"
              className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-semibold leading-none text-white"
            >
              {badge}
            </span>
          )}
        </button>
      )}
    >
      {(close) => (
        <div>
          <div className="flex items-center justify-between px-2.5 py-2">
            <p className="text-sm font-semibold">Notifications</p>
            <button
              type="button"
              disabled={unread === 0 || markAll.isPending}
              onClick={() => markAll.mutate()}
              className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-indigo-600 hover:bg-indigo-50 disabled:opacity-40 dark:text-indigo-400 dark:hover:bg-indigo-500/10"
            >
              <CheckCheck className="h-3.5 w-3.5" /> Mark all read
            </button>
          </div>
          <div className="max-h-96 overflow-y-auto border-t border-zinc-100 dark:border-zinc-800">
            {isLoading ? (
              <div className="flex justify-center py-6">
                <Spinner />
              </div>
            ) : !data?.items.length ? (
              <p className="px-3 py-8 text-center text-sm text-zinc-500">You're all caught up.</p>
            ) : (
              <ul aria-label="Notification list">
                {data.items.map((n) => (
                  <li key={n.id}>
                    <button
                      type="button"
                      data-testid={`notification-${n.id}`}
                      data-read={n.isRead}
                      onClick={() => {
                        if (!n.isRead) markOne.mutate(n.id);
                        if (n.linkUrl?.startsWith('/')) {
                          close();
                          navigate(isCustomer ? n.linkUrl.replace(/^\/tickets\//, '/portal/tickets/') : n.linkUrl);
                        }
                      }}
                      className={cn(
                        'flex w-full gap-3 px-3 py-2.5 text-left transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/60',
                        !n.isRead && 'bg-indigo-50/50 dark:bg-indigo-500/5',
                      )}
                    >
                      <span
                        className={cn('mt-1.5 h-2 w-2 shrink-0 rounded-full', n.isRead ? 'bg-transparent' : 'bg-indigo-500')}
                        aria-label={n.isRead ? undefined : 'Unread'}
                      />
                      <span className="min-w-0 flex-1">
                        <span className={cn('block text-sm', !n.isRead && 'font-semibold')}>{n.title}</span>
                        <span className="block text-xs text-zinc-600 dark:text-zinc-400">{n.message}</span>
                        <span className="mt-0.5 block text-[11px] text-zinc-400">{formatRelative(n.createdAtUtc)}</span>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </Popover>
  );
}
