import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Eye, Hash } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PriorityBadge, SlaBreachedBadge, TicketStatusBadge } from '../../components/domain/TicketBadges';
import { Badge, Card, CardBody, ErrorPanel, Skeleton } from '../../components/ui';
import { formatDateTime } from '../../lib/format';
import { useSessionScope } from '../../lib/queries';
import { useHubEvent, useHubRoom } from '../../lib/signalr';
import { appendComment, ticketKeys, useTicket, useUserNames } from '../../lib/tickets';
import type { AgentPresenceUpdatedEvent, CommentType, PresenceAgent, TicketCommentAddedEvent } from '../../lib/types';
import { useAuthStore } from '../../stores/auth';
import { AiAssistCard } from './AiAssistCard';
import { AttachmentsPanel } from './AttachmentsPanel';
import { TicketControls } from './TicketControls';
import { CommentList, Composer } from './TicketThread';

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join('');
}

/** Who else is on this ticket right now, and who is typing (from `AgentPresenceUpdated`). */
export function PresenceBanner({ agents, meId }: { agents: PresenceAgent[]; meId: string }) {
  const others = agents.filter((a) => a.userId !== meId);
  const typing = others.filter((a) => a.isTyping);
  if (agents.length === 0) return null;

  return (
    <div
      data-testid="presence-banner"
      className="flex flex-wrap items-center gap-3 rounded-lg border border-emerald-200 bg-emerald-50/70 px-3 py-2 text-xs dark:border-emerald-500/30 dark:bg-emerald-500/10"
    >
      <Eye className="h-3.5 w-3.5 text-emerald-600" aria-hidden />
      <div className="flex -space-x-1.5">
        {agents.map((a) => (
          <span
            key={a.userId}
            title={a.userId === meId ? `${a.displayName} (you)` : a.displayName}
            className="flex h-6 w-6 items-center justify-center rounded-full bg-emerald-600 text-[10px] font-semibold text-white ring-2 ring-white dark:ring-zinc-900"
          >
            {initials(a.displayName)}
          </span>
        ))}
      </div>
      <span className="text-emerald-800 dark:text-emerald-200">
        {others.length === 0 ? 'Only you are viewing this ticket' : `Viewing now: ${others.map((a) => a.displayName).join(', ')}`}
      </span>
      {typing.length > 0 && (
        <span className="ml-auto inline-flex items-center gap-1.5 font-medium text-emerald-700 dark:text-emerald-300" aria-live="polite">
          <span className="flex gap-0.5" aria-hidden>
            <span className="h-1 w-1 animate-bounce rounded-full bg-current" />
            <span className="h-1 w-1 animate-bounce rounded-full bg-current [animation-delay:120ms]" />
            <span className="h-1 w-1 animate-bounce rounded-full bg-current [animation-delay:240ms]" />
          </span>
          {typing.map((a) => a.displayName).join(', ')} {typing.length === 1 ? 'is' : 'are'} typing…
        </span>
      )}
    </div>
  );
}

export function TicketDetailPage() {
  const { id = '' } = useParams();
  const me = useAuthStore((s) => s.user)!;
  const scope = useSessionScope();
  const qc = useQueryClient();
  const { data: ticket, isLoading, error, refetch } = useTicket(id);
  const users = useUserNames();

  const [draft, setDraft] = useState('');
  const [replyType, setReplyType] = useState<CommentType>('PublicReply');
  const [presence, setPresence] = useState<PresenceAgent[]>([]);
  const composerRef = useRef<HTMLTextAreaElement>(null);

  // Reset per-ticket state when navigating between tickets.
  useEffect(() => {
    setDraft('');
    setPresence([]);
  }, [id]);

  // JoinTicketRoom on mount / reconnect, LeaveTicketRoom on unmount.
  useHubRoom('tickets', 'JoinTicketRoom', 'LeaveTicketRoom', id);

  useHubEvent<AgentPresenceUpdatedEvent>('tickets', 'AgentPresenceUpdated', (e) => {
    if (e.ticketId === id) setPresence(e.activeAgents);
  });

  useHubEvent<TicketCommentAddedEvent>('tickets', 'TicketCommentAdded', (e) => {
    if (e.ticketId !== id) return;
    appendComment(qc, ticketKeys.detail(id, scope), {
      id: e.commentId,
      authorUserId: e.authorUserId,
      isCustomer: e.isCustomer,
      type: e.type,
      body: e.body,
      createdAtUtc: e.createdAtUtc,
    });
  });

  const insertDraft = (text: string) => {
    setReplyType('PublicReply');
    setDraft((d) => (d.trim() ? `${d.trimEnd()}\n\n${text}` : text));
    requestAnimationFrame(() => composerRef.current?.focus());
  };

  if (error) {
    return (
      <div className="space-y-4">
        <BackLink />
        <ErrorPanel error={error} onRetry={() => void refetch()} />
      </div>
    );
  }

  if (isLoading || !ticket) {
    return (
      <div className="space-y-4">
        <BackLink />
        <Skeleton className="h-24 w-full" />
        <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
          <Skeleton className="h-96 w-full" />
          <Skeleton className="h-96 w-full" />
        </div>
      </div>
    );
  }

  const customer = users.get(ticket.customerId);

  return (
    <div className="space-y-4">
      <BackLink />

      <div className="space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="inline-flex items-center gap-0.5 font-mono text-xs text-zinc-500">
            <Hash className="h-3 w-3" aria-hidden />
            {ticket.ticketNumber}
          </span>
          <TicketStatusBadge status={ticket.status} />
          <PriorityBadge priority={ticket.priority} />
          {ticket.isSlaBreached && <SlaBreachedBadge />}
          <Badge tone="neutral">{ticket.channel}</Badge>
        </div>
        <h1 className="text-xl font-semibold tracking-tight text-zinc-900 dark:text-zinc-50">{ticket.title}</h1>
        <p className="text-xs text-zinc-500">
          Opened {formatDateTime(ticket.createdAtUtc)} by {customer ? `${customer.fullName} (${customer.email})` : 'customer'}
        </p>
      </div>

      <PresenceBanner agents={presence} meId={me.id} />

      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-4">
          <Card>
            <CardBody>
              <p className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-zinc-500">Original request</p>
              <p className="whitespace-pre-wrap text-sm text-zinc-800 dark:text-zinc-200">{ticket.description}</p>
              {ticket.tags.length > 0 && (
                <div className="mt-3 flex flex-wrap gap-1">
                  {ticket.tags.map((t) => (
                    <Badge key={t}>#{t}</Badge>
                  ))}
                </div>
              )}
              {ticket.resolutionNotes && (
                <p className="mt-3 rounded-md bg-emerald-50 px-3 py-2 text-xs text-emerald-800 dark:bg-emerald-500/10 dark:text-emerald-200">
                  <span className="font-semibold">Resolution:</span> {ticket.resolutionNotes}
                </p>
              )}
            </CardBody>
          </Card>

          <CommentList comments={ticket.comments} users={users} />

          <Composer
            ref={composerRef}
            ticketId={ticket.id}
            draft={draft}
            onDraftChange={setDraft}
            type={replyType}
            onTypeChange={setReplyType}
          />
        </div>

        <aside className="space-y-4" aria-label="Ticket sidebar">
          <AiAssistCard ticketId={ticket.id} onInsert={insertDraft} />
          <TicketControls key={ticket.id} ticket={ticket} users={users} />
          <AttachmentsPanel ticketId={ticket.id} />
        </aside>
      </div>
    </div>
  );
}

function BackLink() {
  return (
    <Link to="/tickets" className="inline-flex items-center gap-1 text-xs font-medium text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100">
      <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back to queue
    </Link>
  );
}
