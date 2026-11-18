import { ArrowLeft, Send, ShieldCheck } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PriorityBadge, TicketStatusBadge } from '../../components/domain/TicketBadges';
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, ErrorPanel, Skeleton, Textarea } from '../../components/ui';
import { ApiProblemError } from '../../lib/api';
import { cn } from '../../lib/cn';
import { formatDateTime } from '../../lib/format';
import { usePortalReply, usePortalTicket } from '../../lib/portal';
import { useAuthStore } from '../../stores/auth';

/** Customer ticket view (`GET /api/v1/portal/tickets/{id}`); internal notes are scrubbed server-side. */
export function PortalTicketPage() {
  const { id } = useParams<{ id: string }>();
  const { data: ticket, isLoading, error, refetch } = usePortalTicket(id);
  const me = useAuthStore((s) => s.user);
  const reply = usePortalReply(id ?? '');
  const [draft, setDraft] = useState('');

  const back = (
    <Link to="/portal" className="mb-4 inline-flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100">
      <ArrowLeft className="h-4 w-4" /> My Requests
    </Link>
  );

  if (error) {
    const notFound = error instanceof ApiProblemError && error.status === 404;
    return (
      <>
        {back}
        {notFound ? (
          <Card>
            <EmptyState title="Request not found" description="This request doesn't exist or belongs to another customer." />
          </Card>
        ) : (
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        )}
      </>
    );
  }
  if (isLoading || !ticket) {
    return (
      <>
        {back}
        <Skeleton className="h-80 w-full" />
      </>
    );
  }

  const closed = ticket.status === 'Closed';
  const comments = [...ticket.comments].sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const message = draft.trim();
    if (!message) return;
    try {
      await reply.mutateAsync(message);
      setDraft('');
    } catch {
      /* toasted */
    }
  };

  return (
    <>
      {back}
      <div className="mb-6">
        <p className="font-mono text-xs text-zinc-500">{ticket.ticketNumber}</p>
        <h1 className="mt-0.5 text-xl font-semibold tracking-tight">{ticket.title}</h1>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <TicketStatusBadge status={ticket.status} />
          <PriorityBadge priority={ticket.priority} />
          <span className="text-xs text-zinc-500">Opened {formatDateTime(ticket.createdAtUtc)}</span>
        </div>
      </div>
      <div className="grid gap-4 lg:grid-cols-[1fr_18rem]">
        <div className="space-y-4">
          <Card>
            <CardHeader title="Your request" />
            <CardBody>
              <p className="whitespace-pre-wrap text-sm text-zinc-800 dark:text-zinc-200">{ticket.description}</p>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Conversation" description={`${comments.length} message${comments.length === 1 ? '' : 's'}`} />
            <CardBody className="space-y-3">
              {comments.length === 0 ? (
                <p className="py-4 text-center text-sm text-zinc-500">No replies yet — our team will respond soon.</p>
              ) : (
                <ol className="space-y-3" aria-label="Conversation">
                  {comments.map((c) => {
                    const mine = c.authorUserId === me?.id;
                    return (
                      <li
                        key={c.id}
                        data-testid={`portal-comment-${c.id}`}
                        className={cn(
                          'rounded-xl border p-3.5',
                          c.isCustomer
                            ? 'border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900'
                            : 'border-indigo-200 bg-indigo-50/50 dark:border-indigo-500/30 dark:bg-indigo-500/5',
                        )}
                      >
                        <div className="mb-1.5 flex items-center gap-2 text-xs">
                          <span className="font-semibold">{mine ? 'You' : c.isCustomer ? 'Customer' : 'Support team'}</span>
                          {!c.isCustomer && <Badge tone="indigo">Staff</Badge>}
                          <span className="ml-auto text-zinc-500">{formatDateTime(c.createdAtUtc)}</span>
                        </div>
                        <p className="whitespace-pre-wrap text-sm">{c.body}</p>
                      </li>
                    );
                  })}
                </ol>
              )}
              {closed ? (
                <p className="rounded-lg bg-zinc-100 p-3 text-center text-sm text-zinc-500 dark:bg-zinc-800">This request is closed.</p>
              ) : (
                <form onSubmit={submit} className="space-y-2 border-t border-zinc-100 pt-3 dark:border-zinc-800">
                  <Textarea
                    aria-label="Your reply"
                    rows={3}
                    value={draft}
                    onChange={(e) => setDraft(e.target.value)}
                    placeholder="Add more details or reply to the support team…"
                  />
                  <div className="flex justify-end">
                    <Button type="submit" icon={<Send className="h-4 w-4" />} loading={reply.isPending} disabled={!draft.trim()}>
                      Send reply
                    </Button>
                  </div>
                </form>
              )}
            </CardBody>
          </Card>
        </div>
        <Card className="h-fit">
          <CardHeader title="Details" />
          <CardBody>
            <dl className="space-y-3 text-sm">
              <div>
                <dt className="text-xs text-zinc-500">Status</dt>
                <dd className="mt-0.5">
                  <TicketStatusBadge status={ticket.status} />
                </dd>
              </div>
              <div>
                <dt className="text-xs text-zinc-500">Last updated</dt>
                <dd>{formatDateTime(ticket.updatedAtUtc ?? ticket.createdAtUtc)}</dd>
              </div>
              {ticket.resolvedAtUtc && (
                <div>
                  <dt className="text-xs text-zinc-500">Resolved</dt>
                  <dd>{formatDateTime(ticket.resolvedAtUtc)}</dd>
                </div>
              )}
              {ticket.tags.length > 0 && (
                <div>
                  <dt className="text-xs text-zinc-500">Tags</dt>
                  <dd className="mt-1 flex flex-wrap gap-1">
                    {ticket.tags.map((t) => (
                      <Badge key={t}>{t}</Badge>
                    ))}
                  </dd>
                </div>
              )}
            </dl>
            <p className="mt-5 flex items-start gap-1.5 text-[11px] text-zinc-400" data-testid="scrub-notice">
              <ShieldCheck className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
              Internal staff notes and audit activity are removed by the server before this page receives the ticket.
            </p>
          </CardBody>
        </Card>
      </div>
    </>
  );
}
