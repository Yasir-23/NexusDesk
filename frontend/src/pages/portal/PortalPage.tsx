import { ChevronLeft, ChevronRight, LifeBuoy, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '../../components/layout/AppShell';
import { PriorityBadge, TicketStatusBadge, ticketStatusLabel } from '../../components/domain/TicketBadges';
import { Button, Card, EmptyState, ErrorPanel, Select, Skeleton, Table, TBody, TD, TH, THead, TR } from '../../components/ui';
import { formatRelative } from '../../lib/format';
import { usePortalTickets } from '../../lib/portal';
import { TICKET_STATUSES, type TicketStatus } from '../../lib/types';
import { SubmitRequestDialog } from './SubmitRequestDialog';

const PAGE_SIZE = 20;

/** Customer "My Requests" (`GET /api/v1/portal/tickets`), always scoped server-side to the caller. */
export function PortalPage() {
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState(false);
  const { data, isLoading, error, refetch, isFetching } = usePortalTickets({ status: status || undefined, page, pageSize: PAGE_SIZE });
  const totalPages = Math.max(1, data?.totalPages ?? 1);

  return (
    <>
      <PageHeader
        title="My Requests"
        description="Track the support requests you've opened with us."
        actions={
          <Button icon={<Plus className="h-4 w-4" />} onClick={() => setOpen(true)}>
            Submit New Request
          </Button>
        }
      />
      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-zinc-100 px-5 py-3 dark:border-zinc-800">
          <Select
            aria-label="Filter by status"
            className="w-52"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as TicketStatus | '');
              setPage(1);
            }}
          >
            <option value="">All statuses</option>
            {TICKET_STATUSES.map((s) => (
              <option key={s} value={s}>
                {ticketStatusLabel(s)}
              </option>
            ))}
          </Select>
          <span className="text-xs text-zinc-500">{data ? `${data.totalCount} request${data.totalCount === 1 ? '' : 's'}` : ''}</span>
        </div>
        {error ? (
          <div className="p-5">
            <ErrorPanel error={error} onRetry={() => void refetch()} />
          </div>
        ) : isLoading ? (
          <div className="p-5">
            <Skeleton className="h-48 w-full" />
          </div>
        ) : !data?.items.length ? (
          <EmptyState
            icon={<LifeBuoy className="h-5 w-5" />}
            title={status ? 'No requests with this status' : 'You have no support requests yet'}
            description="Need help? Submit a request and we'll get right on it."
            action={<Button onClick={() => setOpen(true)}>Submit New Request</Button>}
          />
        ) : (
          <Table className={isFetching ? 'opacity-70' : undefined}>
            <THead>
              <tr>
                <TH>Request</TH>
                <TH>Status</TH>
                <TH>Priority</TH>
                <TH>Opened</TH>
                <TH>Last update</TH>
              </tr>
            </THead>
            <TBody>
              {data.items.map((t) => (
                <TR key={t.id} data-testid={`portal-row-${t.ticketNumber}`}>
                  <TD>
                    <Link to={`/portal/tickets/${t.id}`} className="font-medium text-zinc-900 hover:text-indigo-600 dark:text-zinc-100">
                      {t.title}
                    </Link>
                    <span className="block font-mono text-xs text-zinc-500">{t.ticketNumber}</span>
                  </TD>
                  <TD>
                    <TicketStatusBadge status={t.status} />
                  </TD>
                  <TD>
                    <PriorityBadge priority={t.priority} />
                  </TD>
                  <TD className="text-zinc-500">{formatRelative(t.createdAtUtc)}</TD>
                  <TD className="text-zinc-500">{formatRelative(t.updatedAtUtc ?? t.createdAtUtc)}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {totalPages > 1 && (
          <div className="flex items-center justify-between border-t border-zinc-100 px-5 py-3 text-sm dark:border-zinc-800">
            <span className="text-zinc-500">
              Page {page} of {totalPages}
            </span>
            <div className="flex gap-2">
              <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} icon={<ChevronLeft className="h-3.5 w-3.5" />}>
                Previous
              </Button>
              <Button variant="secondary" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next <ChevronRight className="h-3.5 w-3.5" />
              </Button>
            </div>
          </div>
        )}
      </Card>
      <SubmitRequestDialog open={open} onClose={() => setOpen(false)} />
    </>
  );
}
