import { ChevronLeft, ChevronRight, ScrollText, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorPanel,
  ExpandButton,
  Input,
  JsonBlock,
  Select,
  Skeleton,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  useExpanded,
} from '../../components/ui';
import { useAuditLogs } from '../../lib/admin';
import { formatDateTime } from '../../lib/format';
import type { AuditLogEntry } from '../../lib/types';

export const AUDIT_PAGE_SIZE = 50;

const RESOURCE_TYPES = ['', 'ticket', 'user', 'sla_policy'];

function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** `GET /api/v1/audit-logs?actorEmail=&resourceType=&page=&pageSize=50`. */
export function AuditLogPage() {
  const [actorInput, setActorInput] = useState('');
  const actorEmail = useDebounced(actorInput.trim());
  const [resourceType, setResourceType] = useState('');
  const [page, setPage] = useState(1);
  const rows = useExpanded();

  useEffect(() => setPage(1), [actorEmail, resourceType]);

  const { data, isLoading, isFetching, error, refetch } = useAuditLogs({
    actorEmail: actorEmail || undefined,
    resourceType: resourceType || undefined,
    page,
    pageSize: AUDIT_PAGE_SIZE,
  });

  // The API does not apply `actorEmail` yet, so narrow the returned page client-side too.
  const needle = actorEmail.toLowerCase();
  const items = (data?.items ?? []).filter((e) => !needle || (e.actorEmail ?? '').toLowerCase().includes(needle));
  const totalPages = Math.max(1, data?.totalPages ?? 1);

  return (
    <>
      <PageHeader title="Audit Log" description="Security and operations trail for this tenant, newest first." />
      <Card>
        <div className="flex flex-wrap items-center gap-3 border-b border-zinc-100 px-5 py-3 dark:border-zinc-800">
          <div className="relative min-w-56 flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-zinc-400" aria-hidden />
            <Input
              aria-label="Filter by actor email"
              placeholder="Actor email, e.g. admin@acme.com"
              value={actorInput}
              onChange={(e) => setActorInput(e.target.value)}
              className="pl-9"
            />
          </div>
          <Select aria-label="Resource type" value={resourceType} onChange={(e) => setResourceType(e.target.value)} className="w-48">
            {RESOURCE_TYPES.map((t) => (
              <option key={t} value={t}>
                {t || 'All resource types'}
              </option>
            ))}
          </Select>
          <span className="text-xs text-zinc-500" aria-live="polite">
            {data ? `${data.totalCount.toLocaleString('en-US')} entries` : ''}
          </span>
        </div>
        {error ? (
          <div className="p-5">
            <ErrorPanel error={error} onRetry={() => void refetch()} />
          </div>
        ) : isLoading ? (
          <div className="p-5">
            <Skeleton className="h-64 w-full" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState icon={<ScrollText className="h-5 w-5" />} title="No audit entries match" description="Try a different actor or resource type." />
        ) : (
          <Table className={isFetching ? 'opacity-70' : undefined}>
            <THead>
              <tr>
                <TH className="w-8" />
                <TH>When</TH>
                <TH>Actor</TH>
                <TH>Action</TH>
                <TH>Resource</TH>
                <TH>IP</TH>
              </tr>
            </THead>
            <TBody>
              {items.map((e) => (
                <AuditRow key={e.id} entry={e} open={rows.isOpen(e.id)} onToggle={() => rows.toggle(e.id)} />
              ))}
            </TBody>
          </Table>
        )}
        <div className="flex items-center justify-between border-t border-zinc-100 px-5 py-3 text-sm dark:border-zinc-800">
          <span className="text-zinc-500">
            Page {data?.page ?? page} of {totalPages}
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
      </Card>
    </>
  );
}

function AuditRow({ entry: e, open, onToggle }: { entry: AuditLogEntry; open: boolean; onToggle: () => void }) {
  return (
    <>
      <TR data-testid={`audit-row-${e.id}`}>
        <TD>
          <ExpandButton open={open} onClick={onToggle} label={`Show metadata for ${e.action}`} />
        </TD>
        <TD className="whitespace-nowrap text-zinc-500">{formatDateTime(e.createdAtUtc)}</TD>
        <TD>{e.actorEmail ?? <span className="text-zinc-400">system</span>}</TD>
        <TD>
          <Badge tone="indigo" className="font-mono">
            {e.action}
          </Badge>
        </TD>
        <TD className="font-mono text-xs">
          <span className="text-zinc-500">{e.resourceType}</span>
          <span className="block max-w-[16rem] truncate text-zinc-400" title={e.resourceId}>
            {e.resourceId}
          </span>
        </TD>
        <TD className="font-mono text-xs text-zinc-500">{e.ipAddress ?? '—'}</TD>
      </TR>
      {open && (
        <tr className="bg-zinc-50/60 dark:bg-zinc-900/60">
          <td colSpan={6} className="px-4 py-3">
            <p className="mb-1 text-xs font-medium text-zinc-500">metadataJson</p>
            <JsonBlock json={e.metadataJson} />
          </td>
        </tr>
      )}
    </>
  );
}
