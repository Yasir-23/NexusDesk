import { Inbox, Plus, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '../../components/layout/AppShell';
import { PriorityBadge, SlaBreachedBadge, SlaCountdown, TicketStatusBadge, ticketStatusLabel } from '../../components/domain/TicketBadges';
import { Badge, Button, Card, EmptyState, ErrorPanel, Select, Skeleton, Table, TBody, TD, TH, THead, TR } from '../../components/ui';
import { cn } from '../../lib/cn';
import { formatRelative } from '../../lib/format';
import { useTickets, useUserNames } from '../../lib/tickets';
import { TICKET_PRIORITIES, TICKET_STATUSES, type TicketPriority, type TicketStatus, type TicketSummary } from '../../lib/types';
import { useAuthStore } from '../../stores/auth';
import { CreateTicketDialog } from './CreateTicketDialog';

export type QueueTab = 'all' | 'mine' | 'unassigned' | 'breached';

const TABS: { id: QueueTab; label: string }[] = [
  { id: 'all', label: 'All Tickets' },
  { id: 'mine', label: 'My Assigned' },
  { id: 'unassigned', label: 'Unassigned' },
  { id: 'breached', label: 'SLA Breached' },
];

const PAGE_SIZE = 50;

/**
 * `status`, `priority` and "My Assigned" (`assignedAgentId`) are server-side filters.
 * The API has no "unassigned" / "breached" parameter, so those tabs narrow the fetched page.
 */
export function filterQueue(items: TicketSummary[], tab: QueueTab): TicketSummary[] {
  if (tab === 'unassigned') return items.filter((t) => !t.assignedAgentId);
  if (tab === 'breached') return items.filter((t) => t.isSlaBreached);
  return items;
}

export function TicketQueuePage() {
  const me = useAuthStore((s) => s.user)!;
  const [tab, setTab] = useState<QueueTab>('all');
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [priority, setPriority] = useState<TicketPriority | ''>('');
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);

  const query = {
    status: status || undefined,
    priority: priority || undefined,
    assignedAgentId: tab === 'mine' ? me.id : undefined,
    page,
    pageSize: PAGE_SIZE,
  };
  const { data, isLoading, isFetching, error, refetch } = useTickets(query);
  const names = useUserNames();
  const rows = useMemo(() => filterQueue(data?.items ?? [], tab), [data, tab]);

  const changeFilter = (fn: () => void) => {
    fn();
    setPage(1);
  };

  return (
    <>
      <PageHeader
        title="Tickets"
        description="Collaborative queue. New tickets, assignments and SLA breaches stream in live over /hubs/tickets."
        actions={
          <>
            <Button variant="secondary" icon={<RefreshCw className={cn('h-4 w-4', isFetching && 'animate-spin')} />} onClick={() => void refetch()}>
              Refresh
            </Button>
            <Button icon={<Plus className="h-4 w-4" />} onClick={() => setCreateOpen(true)}>
              New ticket
            </Button>
          </>
        }
      />

      <Card>
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-100 px-4 py-3 dark:border-zinc-800">
          <div role="tablist" aria-label="Queue views" className="flex flex-wrap gap-1 rounded-lg bg-zinc-100 p-1 dark:bg-zinc-800/60">
            {TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                role="tab"
                aria-selected={tab === t.id}
                onClick={() => changeFilter(() => setTab(t.id))}
                className={cn(
                  'rounded-md px-3 py-1 text-xs font-medium transition-colors',
                  tab === t.id
                    ? 'bg-white text-zinc-900 shadow-sm dark:bg-zinc-900 dark:text-zinc-100'
                    : 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100',
                )}
              >
                {t.label}
              </button>
            ))}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Select
              aria-label="Filter by status"
              className="h-8 w-44 text-xs"
              value={status}
              onChange={(e) => changeFilter(() => setStatus(e.target.value as TicketStatus | ''))}
            >
              <option value="">All statuses</option>
              {TICKET_STATUSES.map((s) => (
                <option key={s} value={s}>
                  {ticketStatusLabel(s)}
                </option>
              ))}
            </Select>
            <Select
              aria-label="Filter by priority"
              className="h-8 w-36 text-xs"
              value={priority}
              onChange={(e) => changeFilter(() => setPriority(e.target.value as TicketPriority | ''))}
            >
              <option value="">All priorities</option>
              {TICKET_PRIORITIES.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </Select>
          </div>
        </div>

        {error ? (
          <div className="p-4">
            <ErrorPanel error={error} onRetry={() => void refetch()} />
          </div>
        ) : isLoading ? (
          <div className="space-y-2 p-4">
            {Array.from({ length: 5 }, (_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            icon={<Inbox className="h-5 w-5" />}
            title="No tickets match these filters"
            description="Try another view, or create a ticket to see it appear here in real time."
            action={
              <Button size="sm" icon={<Plus className="h-4 w-4" />} onClick={() => setCreateOpen(true)}>
                New ticket
              </Button>
            }
          />
        ) : (
          <Table>
            <THead>
              <tr>
                <TH>Ticket</TH>
                <TH>Status</TH>
                <TH>Priority</TH>
                <TH>Assignee</TH>
                <TH>SLA</TH>
                <TH className="text-right">Updated</TH>
              </tr>
            </THead>
            <TBody>
              {rows.map((t) => (
                <TR key={t.id} data-testid={`ticket-row-${t.ticketNumber}`}>
                  <TD className="max-w-md">
                    <Link to={`/tickets/${t.id}`} className="group block min-w-0">
                      <span className="font-mono text-xs text-zinc-500">{t.ticketNumber}</span>
                      <span className="block truncate font-medium text-zinc-900 group-hover:text-indigo-600 dark:text-zinc-100 dark:group-hover:text-indigo-400">
                        {t.title}
                      </span>
                    </Link>
                    {t.tags.length > 0 && (
                      <div className="mt-1 flex flex-wrap gap-1">
                        {t.tags.slice(0, 4).map((tag) => (
                          <span key={tag} className="rounded bg-zinc-100 px-1.5 text-[11px] text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400">
                            #{tag}
                          </span>
                        ))}
                      </div>
                    )}
                  </TD>
                  <TD>
                    <TicketStatusBadge status={t.status} />
                  </TD>
                  <TD>
                    <PriorityBadge priority={t.priority} />
                  </TD>
                  <TD className="text-xs">
                    {t.assignedAgentId ? (
                      <span>{t.assignedAgentId === me.id ? 'You' : (names.get(t.assignedAgentId)?.fullName ?? 'Agent')}</span>
                    ) : (
                      <Badge tone="amber">Unassigned</Badge>
                    )}
                  </TD>
                  <TD>
                    {t.isSlaBreached ? (
                      <SlaBreachedBadge />
                    ) : t.status === 'Resolved' || t.status === 'Closed' ? (
                      <Badge tone="green">Done</Badge>
                    ) : (
                      <SlaCountdown
                        label={t.firstRespondedAtUtc ? 'Resolve' : 'Respond'}
                        dueUtc={t.firstRespondedAtUtc ? t.resolutionDueUtc : t.firstResponseDueUtc}
                        metAtUtc={null}
                      />
                    )}
                  </TD>
                  <TD className="whitespace-nowrap text-right text-xs text-zinc-500">{formatRelative(t.updatedAtUtc ?? t.createdAtUtc)}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}

        {data && (
          <div className="flex items-center justify-between gap-3 border-t border-zinc-100 px-4 py-2.5 text-xs text-zinc-500 dark:border-zinc-800">
            <span>
              Showing {rows.length} of {data.totalCount} tickets
              {(tab === 'unassigned' || tab === 'breached') && ' (filtered from this page)'}
            </span>
            {data.totalPages > 1 && (
              <div className="flex items-center gap-2">
                <Button size="sm" variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                  Previous
                </Button>
                <span>
                  Page {data.page} of {data.totalPages}
                </span>
                <Button size="sm" variant="secondary" disabled={page >= data.totalPages} onClick={() => setPage((p) => p + 1)}>
                  Next
                </Button>
              </div>
            )}
          </div>
        )}
      </Card>

      <CreateTicketDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </>
  );
}
