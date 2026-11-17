import { useEffect, useState } from 'react';
import { AlarmClock, ArrowDown, ArrowUp, ChevronsUp, Minus, ShieldAlert } from 'lucide-react';
import { Badge, type BadgeTone } from '../ui';
import type { ChatSessionStatus, KbArticleStatus, TicketPriority, TicketStatus } from '../../lib/types';

const STATUS_LABELS: Record<TicketStatus, string> = {
  New: 'New',
  Open: 'Open',
  PendingCustomer: 'Pending customer',
  PendingInternal: 'Pending internal',
  Resolved: 'Resolved',
  Closed: 'Closed',
};

const STATUS_TONES: Record<TicketStatus, BadgeTone> = {
  New: 'sky',
  Open: 'indigo',
  PendingCustomer: 'amber',
  PendingInternal: 'violet',
  Resolved: 'green',
  Closed: 'neutral',
};

export function ticketStatusLabel(status: TicketStatus) {
  return STATUS_LABELS[status] ?? status;
}

export function TicketStatusBadge({ status }: { status: TicketStatus }) {
  return (
    <Badge tone={STATUS_TONES[status] ?? 'neutral'} dot>
      {ticketStatusLabel(status)}
    </Badge>
  );
}

const PRIORITY_META: Record<TicketPriority, { tone: BadgeTone; icon: typeof Minus }> = {
  Low: { tone: 'neutral', icon: ArrowDown },
  Medium: { tone: 'sky', icon: Minus },
  High: { tone: 'amber', icon: ArrowUp },
  Urgent: { tone: 'red', icon: ChevronsUp },
};

export function PriorityBadge({ priority }: { priority: TicketPriority }) {
  const meta = PRIORITY_META[priority] ?? PRIORITY_META.Medium;
  const Icon = meta.icon;
  return (
    <Badge tone={meta.tone} icon={<Icon className="h-3 w-3" aria-hidden />}>
      {priority}
    </Badge>
  );
}

const CHAT_TONES: Record<ChatSessionStatus, BadgeTone> = {
  Waiting: 'amber',
  Active: 'green',
  Closed: 'neutral',
  ConvertedToTicket: 'violet',
};

export function ChatStatusBadge({ status }: { status: ChatSessionStatus }) {
  return (
    <Badge tone={CHAT_TONES[status] ?? 'neutral'} dot>
      {status === 'ConvertedToTicket' ? 'Converted to ticket' : status}
    </Badge>
  );
}

export function KbStatusBadge({ status }: { status: KbArticleStatus }) {
  const tone: Record<KbArticleStatus, BadgeTone> = { Draft: 'amber', Published: 'green', Archived: 'neutral' };
  return <Badge tone={tone[status] ?? 'neutral'}>{status}</Badge>;
}

/** Re-renders every `intervalMs` so countdowns stay current. */
export function useNow(intervalMs = 30_000) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(t);
  }, [intervalMs]);
  return now;
}

export function formatDuration(ms: number): string {
  const totalMinutes = Math.max(0, Math.round(Math.abs(ms) / 60_000));
  const d = Math.floor(totalMinutes / 1440);
  const h = Math.floor((totalMinutes % 1440) / 60);
  const m = totalMinutes % 60;
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

/**
 * SLA target countdown: `met` once the target event happened, otherwise the time left
 * (amber under an hour) or how long it has been overdue.
 */
export function SlaCountdown({ dueUtc, metAtUtc, label }: { dueUtc: string | null; metAtUtc: string | null; label: string }) {
  const now = useNow();
  if (!dueUtc) return <Badge tone="neutral">{label}: no target</Badge>;
  const due = new Date(dueUtc).getTime();
  if (metAtUtc) {
    const onTime = new Date(metAtUtc).getTime() <= due;
    return <Badge tone={onTime ? 'green' : 'red'}>{label}: {onTime ? 'met' : 'missed'}</Badge>;
  }
  const left = due - now;
  if (left < 0) {
    return (
      <Badge tone="red" icon={<AlarmClock className="h-3 w-3" aria-hidden />}>
        {label}: overdue {formatDuration(left)}
      </Badge>
    );
  }
  return (
    <Badge tone={left < 3_600_000 ? 'amber' : 'teal'} icon={<AlarmClock className="h-3 w-3" aria-hidden />}>
      {label}: {formatDuration(left)} left
    </Badge>
  );
}

export function SlaBreachedBadge() {
  return (
    <Badge tone="red" icon={<ShieldAlert className="h-3 w-3" aria-hidden />}>
      SLA breached
    </Badge>
  );
}
