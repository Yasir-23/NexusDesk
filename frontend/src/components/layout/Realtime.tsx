import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { chatKeys } from '../../lib/chat';
import { cn } from '../../lib/cn';
import { startRealtimeSync, useHubEvent, useRealtimeStatus, type RealtimeStatus } from '../../lib/signalr';
import { ticketKeys } from '../../lib/tickets';
import type { SlaBreachAlertEvent, TicketRealtimeEvent } from '../../lib/types';
import { useAuthStore } from '../../stores/auth';
import { toast } from '../../stores/toast';

/**
 * Mounted once inside the authenticated shell: keeps both hubs connected for the current
 * identity and turns tenant-wide hub events into cache invalidations + alerts.
 * Page-specific events (presence, typing, thread messages) are handled by the pages.
 */
export function RealtimeBridge() {
  const qc = useQueryClient();

  useEffect(() => startRealtimeSync(), []);

  const refreshTicket = (e: TicketRealtimeEvent) => {
    void qc.invalidateQueries({ queryKey: ticketKeys.listRoot });
    void qc.invalidateQueries({ queryKey: ticketKeys.detailRoot(e.ticketId) });
  };
  useHubEvent<TicketRealtimeEvent>('tickets', 'TicketCreated', refreshTicket);
  useHubEvent<TicketRealtimeEvent>('tickets', 'TicketUpdated', refreshTicket);
  useHubEvent<SlaBreachAlertEvent>('tickets', 'SlaBreachAlert', (e) => {
    toast.warning(`SLA breach · ${e.ticketNumber}`, `${e.priority} priority ticket missed its ${humanBreach(e.breachType)} target.`);
    void qc.invalidateQueries({ queryKey: ticketKeys.listRoot });
    void qc.invalidateQueries({ queryKey: ticketKeys.detailRoot(e.ticketId) });
  });

  const refreshChats = () => void qc.invalidateQueries({ queryKey: chatKeys.sessionsRoot });
  useHubEvent('chat', 'ChatSessionClaimed', refreshChats);
  useHubEvent('chat', 'ChatSessionClosed', refreshChats);

  return null;
}

function humanBreach(breachType: string) {
  if (/first/i.test(breachType)) return 'first-response';
  if (/resol/i.test(breachType)) return 'resolution';
  return breachType;
}

function combine(a: RealtimeStatus, b: RealtimeStatus): RealtimeStatus {
  const order: RealtimeStatus[] = ['disconnected', 'reconnecting', 'connecting', 'idle', 'connected'];
  return order.indexOf(a) < order.indexOf(b) ? a : b;
}

/** Header pill: green "Live" when both hubs are connected, amber while (re)connecting. */
export function RealtimeIndicator() {
  const tenant = useAuthStore((s) => s.tenantSubdomain);
  const status = combine(useRealtimeStatus('tickets'), useRealtimeStatus('chat'));
  if (!tenant || status === 'idle') return null;

  const live = status === 'connected';
  const pending = status === 'connecting' || status === 'reconnecting';
  const label = live ? 'Live' : pending ? 'Reconnecting' : 'Offline';

  return (
    <span
      data-testid="realtime-status"
      title={live ? 'Connected to /hubs/tickets and /hubs/chat' : `Real-time hubs: ${status}`}
      className={cn(
        'hidden items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset sm:inline-flex',
        live && 'text-emerald-700 ring-emerald-600/20 dark:text-emerald-300 dark:ring-emerald-400/30',
        pending && 'text-amber-700 ring-amber-600/20 dark:text-amber-300 dark:ring-amber-400/30',
        !live && !pending && 'text-zinc-500 ring-zinc-300 dark:ring-zinc-700',
      )}
    >
      <span className="relative flex h-2 w-2" aria-hidden>
        {live && <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60 motion-reduce:hidden" />}
        <span
          className={cn(
            'relative inline-flex h-2 w-2 rounded-full',
            live ? 'bg-emerald-500' : pending ? 'animate-pulse bg-amber-500' : 'bg-zinc-400',
          )}
        />
      </span>
      {label}
    </span>
  );
}
