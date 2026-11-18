import { BarChart3, Download, LineChart, Table2, Trophy } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import { formatDuration } from '../../components/domain/TicketBadges';
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorPanel,
  Skeleton,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
} from '../../components/ui';
import { ApiProblemError, notifyError } from '../../lib/api';
import { LOOKBACK_WINDOWS, exportTicketsCsv, useAgentPerformance, useAnalyticsOverview, useTicketTrends, type LookbackDays } from '../../lib/analytics';
import { cn } from '../../lib/cn';
import { formatNumber } from '../../lib/format';
import type { AnalyticsOverview } from '../../lib/types';
import { toast } from '../../stores/toast';
import { TREND_SERIES, TrendChart, formatTrendDate } from './TrendChart';

const fmtMinutes = (m: number) => (m > 0 ? formatDuration(m * 60_000) : '—');
const fmtPct = (p: number) => `${p.toFixed(1)}%`;

const KPIS: { key: keyof AnalyticsOverview; label: string; format: (v: number) => string; hint?: string }[] = [
  { key: 'totalTickets', label: 'Total tickets', format: formatNumber },
  { key: 'openTickets', label: 'Open tickets', format: formatNumber },
  { key: 'resolvedTickets', label: 'Resolved tickets', format: formatNumber },
  { key: 'slaComplianceRate', label: 'SLA compliance', format: fmtPct },
  { key: 'averageFirstResponseMinutes', label: 'Avg. first response', format: fmtMinutes },
  { key: 'averageResolutionMinutes', label: 'Avg. resolution', format: fmtMinutes },
  { key: 'csatAverageScore', label: 'CSAT', format: (v) => (v > 0 ? `${v.toFixed(2)} / 5` : '—') },
  { key: 'activeChatSessions', label: 'Active chats', format: formatNumber, hint: 'right now' },
  { key: 'publishedKbArticles', label: 'Published KB articles', format: formatNumber, hint: 'all time' },
];

export function AnalyticsPage() {
  const [days, setDays] = useState<LookbackDays>(30);
  const [exporting, setExporting] = useState(false);

  const onExport = async () => {
    setExporting(true);
    try {
      const { fileName } = await exportTicketsCsv(days);
      toast.success('CSV exported', `${fileName} · last ${days} days`);
    } catch (err) {
      if (err instanceof ApiProblemError) notifyError(err);
      else toast.error('Export failed', String(err));
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Analytics"
        description="Ticket volume, SLA performance and team throughput for this tenant."
        actions={
          <>
            <div role="radiogroup" aria-label="Lookback window" className="flex rounded-lg bg-zinc-100 p-0.5 dark:bg-zinc-800">
              {LOOKBACK_WINDOWS.map((d) => (
                <button
                  key={d}
                  type="button"
                  role="radio"
                  aria-checked={days === d}
                  onClick={() => setDays(d)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-xs font-medium transition-colors',
                    days === d ? 'bg-white text-zinc-900 shadow-sm dark:bg-zinc-950 dark:text-zinc-100' : 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400',
                  )}
                >
                  {d} days
                </button>
              ))}
            </div>
            <Button variant="secondary" icon={<Download className="h-4 w-4" />} loading={exporting} onClick={() => void onExport()}>
              Export CSV
            </Button>
          </>
        }
      />
      <div className="space-y-4">
        <KpiGrid days={days} />
        <TrendsCard days={days} />
        <AgentScoreboard days={days} />
      </div>
    </>
  );
}

function KpiGrid({ days }: { days: number }) {
  const { data, error, refetch } = useAnalyticsOverview(days);
  if (error) return <ErrorPanel error={error} onRetry={() => void refetch()} />;
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-5" aria-label="Key metrics">
      {KPIS.map((k) => (
        <Card key={k.key} className="px-4 py-3" data-testid={`kpi-${k.key}`}>
          <p className="text-xs font-medium text-zinc-500">{k.label}</p>
          {data ? (
            <p className="mt-1 text-2xl font-semibold tabular-nums tracking-tight">{k.format(data[k.key])}</p>
          ) : (
            <Skeleton className="mt-2 h-7 w-20" />
          )}
          <p className="mt-0.5 text-[11px] text-zinc-400">{k.hint ?? `last ${days} days`}</p>
        </Card>
      ))}
    </div>
  );
}

function TrendsCard({ days }: { days: number }) {
  const { data, error, refetch, isLoading } = useTicketTrends(days);
  const [view, setView] = useState<'chart' | 'table'>('chart');
  const totals = (data ?? []).reduce(
    (acc, d) => ({ created: acc.created + d.createdCount, resolved: acc.resolved + d.resolvedCount, breached: acc.breached + d.breachedCount }),
    { created: 0, resolved: 0, breached: 0 },
  );

  return (
    <Card>
      <CardHeader
        title="Daily ticket trends"
        icon={<BarChart3 className="h-4 w-4" />}
        description={data ? `${formatNumber(totals.created)} created · ${formatNumber(totals.resolved)} resolved · ${formatNumber(totals.breached)} SLA breaches` : undefined}
        actions={
          <div role="group" aria-label="Trend view" className="flex gap-1">
            <ViewButton active={view === 'chart'} onClick={() => setView('chart')} icon={<LineChart className="h-3.5 w-3.5" />}>
              Chart
            </ViewButton>
            <ViewButton active={view === 'table'} onClick={() => setView('table')} icon={<Table2 className="h-3.5 w-3.5" />}>
              Table
            </ViewButton>
          </div>
        }
      />
      {error ? (
        <CardBody>
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        </CardBody>
      ) : isLoading || !data ? (
        <CardBody>
          <Skeleton className="h-64 w-full" />
        </CardBody>
      ) : data.length === 0 ? (
        <EmptyState icon={<BarChart3 className="h-5 w-5" />} title="No ticket activity in this window" />
      ) : view === 'chart' ? (
        <CardBody>
          <TrendChart data={data} />
        </CardBody>
      ) : (
        <div className="max-h-96 overflow-y-auto">
          <Table aria-label="Daily ticket trends table">
            <THead>
              <tr>
                <TH>Date</TH>
                {TREND_SERIES.map((s) => (
                  <TH key={s.key} className="text-right">
                    {s.label}
                  </TH>
                ))}
              </tr>
            </THead>
            <TBody>
              {data.map((d) => (
                <TR key={d.date} data-testid={`trend-row-${d.date}`}>
                  <TD>{formatTrendDate(d.date)}</TD>
                  <TD className="text-right tabular-nums">{d.createdCount}</TD>
                  <TD className="text-right tabular-nums">{d.resolvedCount}</TD>
                  <TD className="text-right tabular-nums">{d.breachedCount}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
      )}
    </Card>
  );
}

function ViewButton({ active, onClick, icon, children }: { active: boolean; onClick: () => void; icon: ReactNode; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium',
        active ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900' : 'text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800',
      )}
    >
      {icon}
      {children}
    </button>
  );
}

function AgentScoreboard({ days }: { days: number }) {
  const { data, error, refetch, isLoading } = useAgentPerformance(days);
  const rows = [...(data ?? [])].sort((a, b) => b.resolvedTickets - a.resolvedTickets || b.slaComplianceRate - a.slaComplianceRate);

  return (
    <Card>
      <CardHeader title="Agent performance" icon={<Trophy className="h-4 w-4" />} description="Ranked by tickets resolved in the window." />
      {error ? (
        <CardBody>
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        </CardBody>
      ) : isLoading || !data ? (
        <CardBody>
          <Skeleton className="h-32 w-full" />
        </CardBody>
      ) : rows.length === 0 ? (
        <EmptyState icon={<Trophy className="h-5 w-5" />} title="No agent activity in this window" />
      ) : (
        <Table>
          <THead>
            <tr>
              <TH className="w-10">#</TH>
              <TH>Agent</TH>
              <TH className="text-right">Assigned</TH>
              <TH className="text-right">Resolved</TH>
              <TH className="text-right">Resolution rate</TH>
              <TH className="text-right">Avg. resolution</TH>
              <TH className="text-right">SLA compliance</TH>
            </tr>
          </THead>
          <TBody>
            {rows.map((a, i) => (
              <TR key={a.agentUserId} data-testid={`agent-row-${a.agentUserId}`}>
                <TD className="tabular-nums text-zinc-500">{i + 1}</TD>
                <TD className="font-medium">{a.agentName}</TD>
                <TD className="text-right tabular-nums">{a.assignedTickets}</TD>
                <TD className="text-right tabular-nums">{a.resolvedTickets}</TD>
                <TD className="text-right tabular-nums">{a.assignedTickets ? fmtPct((a.resolvedTickets / a.assignedTickets) * 100) : '—'}</TD>
                <TD className="text-right tabular-nums">{fmtMinutes(a.averageResolutionMinutes)}</TD>
                <TD className="text-right">
                  <Badge tone={a.slaComplianceRate >= 95 ? 'green' : a.slaComplianceRate >= 80 ? 'amber' : 'red'}>{fmtPct(a.slaComplianceRate)}</Badge>
                </TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  );
}
