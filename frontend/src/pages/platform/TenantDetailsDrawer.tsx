import { Eye, History, Power, PowerOff } from 'lucide-react';
import { IsolationBadge, PlanBadge, StatusBadge } from '../../components/domain/Badges';
import { Button, Dialog, EmptyState, ErrorPanel, Skeleton } from '../../components/ui';
import { formatBytes, formatDateTime, formatNumber, formatRelative } from '../../lib/format';
import { usePlatformTenant } from '../../lib/queries';
import { useAuthStore } from '../../stores/auth';
import { toast } from '../../stores/toast';
import { cn } from '../../lib/cn';
import type { StatusTarget } from './StatusChangeDialog';

function actionTone(action: string) {
  if (/suspend/i.test(action)) return 'bg-rose-500';
  if (/active|provision/i.test(action)) return 'bg-emerald-500';
  return 'bg-indigo-500';
}

/** `GET /api/v1/platform/tenants/{tenantId}` — metadata + tenant audit log. */
export function TenantDetailsDrawer({
  tenantId,
  onClose,
  onChangeStatus,
}: {
  tenantId: string | null;
  onClose: () => void;
  onChangeStatus: (t: StatusTarget) => void;
}) {
  const { data, isLoading, error, refetch } = usePlatformTenant(tenantId);
  const setTenant = useAuthStore((s) => s.setTenantSubdomain);
  const t = data?.tenant;

  return (
    <Dialog
      open={tenantId !== null}
      onClose={onClose}
      variant="side"
      title={t ? t.name : 'Tenant details'}
      description={t && <span className="font-mono">{t.subdomain}</span>}
      footer={
        t && (
          <>
            <Button
              variant="secondary"
              icon={<Eye className="h-4 w-4" />}
              onClick={() => {
                setTenant(t.subdomain);
                toast.info(`Scoped to '${t.subdomain}'`, 'X-Tenant-Subdomain now targets this tenant.');
              }}
            >
              Inspect context
            </Button>
            <Button
              variant={t.status === 'Active' ? 'danger' : 'success'}
              icon={t.status === 'Active' ? <PowerOff className="h-4 w-4" /> : <Power className="h-4 w-4" />}
              onClick={() => onChangeStatus({ id: t.id, name: t.name, subdomain: t.subdomain, status: t.status })}
            >
              {t.status === 'Active' ? 'Suspend' : 'Reactivate'}
            </Button>
          </>
        )
      }
    >
      {isLoading ? (
        <div className="space-y-3">
          <Skeleton className="h-5 w-1/2" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : error ? (
        <ErrorPanel error={error} onRetry={() => void refetch()} />
      ) : t ? (
        <div className="space-y-6">
          <div className="flex flex-wrap gap-1.5">
            <StatusBadge status={t.status} />
            <PlanBadge plan={t.planTier} />
            <IsolationBadge strategy={t.isolationStrategy} />
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2.5 text-sm">
            <dt className="text-zinc-500">Tenant ID</dt>
            <dd className="break-all font-mono text-xs">{t.id}</dd>
            <dt className="text-zinc-500">Created</dt>
            <dd>{formatDateTime(t.createdAtUtc)}</dd>
            <dt className="text-zinc-500">Updated</dt>
            <dd>{formatDateTime(t.updatedAtUtc)}</dd>
          </dl>

          <div className="grid grid-cols-3 gap-2">
            {[
              ['Agents', formatNumber(t.maxAgents)],
              ['Tickets / mo', formatNumber(t.maxTicketsPerMonth)],
              ['Storage', formatBytes(t.maxStorageBytes)],
            ].map(([label, value]) => (
              <div key={label} className="rounded-lg border border-zinc-200 p-3 dark:border-zinc-800">
                <p className="text-[11px] uppercase tracking-wide text-zinc-500">{label}</p>
                <p className="mt-1 text-sm font-semibold tabular-nums">{value}</p>
              </div>
            ))}
          </div>

          <div>
            <h3 className="mb-3 flex items-center gap-2 text-sm font-semibold">
              <History className="h-4 w-4 text-zinc-400" /> Audit history
              <span className="text-xs font-normal text-zinc-400">({data.auditLogs.length})</span>
            </h3>
            {data.auditLogs.length === 0 ? (
              <EmptyState title="No audit entries yet" />
            ) : (
              <ol className="relative space-y-4 border-l border-zinc-200 pl-5 dark:border-zinc-800">
                {[...data.auditLogs]
                  .sort((a, b) => b.timestampUtc.localeCompare(a.timestampUtc))
                  .map((log) => (
                    <li key={log.id} className="relative">
                      <span
                        className={cn(
                          'absolute -left-[25px] top-1 h-2.5 w-2.5 rounded-full ring-4 ring-white dark:ring-zinc-900',
                          actionTone(log.action),
                        )}
                      />
                      <div className="flex flex-wrap items-baseline justify-between gap-x-3">
                        <p className="text-sm font-medium">{log.action}</p>
                        <time className="text-xs text-zinc-400" dateTime={log.timestampUtc} title={formatDateTime(log.timestampUtc)}>
                          {formatRelative(log.timestampUtc)}
                        </time>
                      </div>
                      {log.reason && <p className="mt-0.5 text-sm text-zinc-600 dark:text-zinc-400">{log.reason}</p>}
                      <p className="mt-0.5 text-xs text-zinc-400">by {log.performedBy}</p>
                    </li>
                  ))}
              </ol>
            )}
          </div>
        </div>
      ) : null}
    </Dialog>
  );
}
