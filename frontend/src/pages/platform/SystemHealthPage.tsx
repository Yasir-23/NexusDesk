import { Activity, Boxes, Cpu, Database, HeartPulse, RefreshCw, Server, Zap } from 'lucide-react';
import type { ReactNode } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import { Badge, Button, Card, CardBody, CardHeader, ErrorPanel, Skeleton } from '../../components/ui';
import { formatDateTime } from '../../lib/format';
import { useHealthProbe, useSystemInfo } from '../../lib/queries';
import { cn } from '../../lib/cn';
import type { ProbeResult } from '../../lib/api';

function healthTone(status: string | undefined) {
  switch (status) {
    case 'Healthy':
      return 'green' as const;
    case 'Degraded':
      return 'amber' as const;
    default:
      return 'red' as const;
  }
}

interface HealthEntry {
  status: string;
  description?: string | null;
  durationMs?: number;
}

interface HealthReport {
  status?: string;
  totalDurationMs?: number;
  entries?: Record<string, HealthEntry>;
}

function asReport(probe: ProbeResult | undefined): HealthReport | null {
  return probe && probe.body && typeof probe.body === 'object' ? (probe.body as HealthReport) : null;
}

function ConnectivityTile({ label, status, icon }: { label: string; status: string; icon: ReactNode }) {
  const healthy = status === 'Healthy';
  return (
    <div className="flex items-center gap-3 rounded-lg border border-zinc-200 p-4 dark:border-zinc-800">
      <div className={cn('rounded-lg p-2', healthy ? 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10' : 'bg-rose-50 text-rose-600 dark:bg-rose-500/10')}>
        {icon}
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{label}</p>
        <p className="text-xs text-zinc-500">{healthy ? 'Connection verified' : 'Check failed'}</p>
      </div>
      <Badge tone={healthTone(status)} dot>
        {status}
      </Badge>
    </div>
  );
}

/** `GET /api/v1/system/info` + `GET /health/ready` + `GET /health/live`. */
export function SystemHealthPage() {
  const info = useSystemInfo();
  const ready = useHealthProbe('/health/ready');
  const live = useHealthProbe('/health/live');
  const report = asReport(ready.data);

  const refreshAll = () => {
    void info.refetch();
    void ready.refetch();
    void live.refetch();
  };
  const fetching = info.isFetching || ready.isFetching || live.isFetching;

  return (
    <>
      <PageHeader
        title="System Health"
        description="Live runtime diagnostics, loaded modules and infrastructure probes. Auto-refreshes every 15 seconds."
        actions={
          <Button variant="secondary" icon={<RefreshCw className={cn('h-4 w-4', fetching && 'animate-spin')} />} onClick={refreshAll}>
            Refresh
          </Button>
        }
      />

      <div className="grid gap-4 lg:grid-cols-3">
        {/* Probes */}
        {[
          { label: 'Readiness', path: '/health/ready', q: ready, icon: <HeartPulse className="h-4 w-4" /> },
          { label: 'Liveness', path: '/health/live', q: live, icon: <Activity className="h-4 w-4" /> },
        ].map(({ label, path, q, icon }) => {
          const r = asReport(q.data);
          const status = r?.status ?? (q.data?.ok ? 'Healthy' : q.data ? 'Unhealthy' : undefined);
          return (
            <Card key={path} className="p-5">
              <div className="flex items-center justify-between">
                <span className="flex items-center gap-2 text-sm font-medium text-zinc-600 dark:text-zinc-300">
                  {icon} {label}
                </span>
                <code className="font-mono text-[11px] text-zinc-400">{path}</code>
              </div>
              {q.isLoading ? (
                <Skeleton className="mt-4 h-8 w-32" />
              ) : (
                <div className="mt-3 flex items-end justify-between">
                  <span
                    className={cn(
                      'text-2xl font-semibold',
                      status === 'Healthy' ? 'text-emerald-600 dark:text-emerald-400' : status === 'Degraded' ? 'text-amber-600' : 'text-rose-600',
                    )}
                  >
                    {status ?? 'Unknown'}
                  </span>
                  <span className="text-xs tabular-nums text-zinc-500">
                    HTTP {q.data?.status || '—'} · {q.data?.latencyMs ?? '—'} ms
                  </span>
                </div>
              )}
            </Card>
          );
        })}
        <Card className="p-5">
          <div className="flex items-center justify-between">
            <span className="flex items-center gap-2 text-sm font-medium text-zinc-600 dark:text-zinc-300">
              <Zap className="h-4 w-4" /> Overall connectivity
            </span>
            <code className="font-mono text-[11px] text-zinc-400">/api/v1/system/info</code>
          </div>
          {info.isLoading ? (
            <Skeleton className="mt-4 h-8 w-32" />
          ) : (
            <div className="mt-3 flex items-end justify-between">
              <span className={cn('text-2xl font-semibold', info.data?.connectivity.overall === 'Healthy' ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600')}>
                {info.data?.connectivity.overall ?? 'Unknown'}
              </span>
              <span className="text-xs text-zinc-500">{info.data ? formatDateTime(info.data.timestampUtc) : ''}</span>
            </div>
          )}
        </Card>
      </div>

      {info.error && (
        <div className="mt-4">
          <ErrorPanel error={info.error} onRetry={() => void info.refetch()} />
        </div>
      )}

      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader title="Infrastructure" description="PostgreSQL (with pgvector) and Redis connectivity" icon={<Server className="h-4 w-4" />} />
          <CardBody className="space-y-3">
            {info.data ? (
              <>
                <ConnectivityTile label="PostgreSQL" status={info.data.connectivity.postgresql} icon={<Database className="h-4 w-4" />} />
                <ConnectivityTile label="Redis" status={info.data.connectivity.redis} icon={<Zap className="h-4 w-4" />} />
              </>
            ) : (
              <>
                <Skeleton className="h-16 w-full" />
                <Skeleton className="h-16 w-full" />
              </>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Runtime" description="Host process diagnostics" icon={<Cpu className="h-4 w-4" />} />
          <CardBody>
            {info.data ? (
              <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 text-sm">
                <dt className="text-zinc-500">Application</dt>
                <dd className="font-medium">{info.data.application}</dd>
                <dt className="text-zinc-500">Environment</dt>
                <dd>
                  <Badge tone={info.data.environment === 'Production' ? 'violet' : 'sky'}>{info.data.environment}</Badge>
                </dd>
                <dt className="text-zinc-500">Framework</dt>
                <dd className="font-mono text-xs">{info.data.framework}</dd>
                <dt className="text-zinc-500">OS</dt>
                <dd className="font-mono text-xs">{info.data.osVersion}</dd>
                <dt className="text-zinc-500">Architecture</dt>
                <dd className="font-mono text-xs">{info.data.processArchitecture}</dd>
              </dl>
            ) : (
              <Skeleton className="h-32 w-full" />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Loaded modules"
            description="Modular monolith — each bounded context registers itself at startup"
            icon={<Boxes className="h-4 w-4" />}
          />
          <CardBody>
            {info.data ? (
              <ul className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                {info.data.loadedModules.map((m) => (
                  <li
                    key={m}
                    className="flex items-center gap-2 rounded-lg border border-zinc-200 px-3 py-2 text-sm dark:border-zinc-800"
                  >
                    <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" aria-hidden />
                    {m}
                  </li>
                ))}
              </ul>
            ) : (
              <Skeleton className="h-20 w-full" />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Readiness checks"
            description={report?.totalDurationMs != null ? `Completed in ${report.totalDurationMs.toFixed(0)} ms` : 'Individual health check results'}
            icon={<HeartPulse className="h-4 w-4" />}
          />
          <CardBody className="p-0">
            {report?.entries ? (
              <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
                {Object.entries(report.entries).map(([name, entry]) => (
                  <li key={name} className="flex items-center gap-3 px-5 py-3">
                    <div className="min-w-0 flex-1">
                      <p className="font-mono text-xs font-medium">{name}</p>
                      {entry.description && <p className="truncate text-xs text-zinc-500">{entry.description}</p>}
                    </div>
                    {entry.durationMs != null && <span className="text-xs tabular-nums text-zinc-400">{entry.durationMs.toFixed(0)} ms</span>}
                    <Badge tone={healthTone(entry.status)} dot>
                      {entry.status}
                    </Badge>
                  </li>
                ))}
              </ul>
            ) : ready.isLoading ? (
              <div className="p-5">
                <Skeleton className="h-24 w-full" />
              </div>
            ) : (
              <pre className="overflow-x-auto p-5 font-mono text-xs text-zinc-500">{JSON.stringify(ready.data?.body, null, 2)}</pre>
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}
