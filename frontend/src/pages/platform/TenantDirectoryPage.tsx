import { useMemo, useState, type ReactNode } from 'react';
import {
  Building2,
  ChevronLeft,
  ChevronRight,
  DatabaseZap,
  PauseCircle,
  Plus,
  Power,
  PowerOff,
  RefreshCw,
  Search,
  ShieldCheck,
} from 'lucide-react';
import { PageHeader } from '../../components/layout/AppShell';
import { IsolationBadge, PlanBadge, StatusBadge } from '../../components/domain/Badges';
import { Button, Card, EmptyState, ErrorPanel, Input, Select, Skeleton, Table, TBody, TD, TH, THead, TR } from '../../components/ui';
import { formatBytes, formatDate, formatQuota } from '../../lib/format';
import { usePlatformTenants } from '../../lib/queries';
import { cn } from '../../lib/cn';
import type { TenantPlanTier, TenantStatus } from '../../lib/types';
import { ProvisionTenantDialog } from './ProvisionTenantDialog';
import { StatusChangeDialog, type StatusTarget } from './StatusChangeDialog';
import { TenantDetailsDrawer } from './TenantDetailsDrawer';

const PAGE_SIZE = 100; // backend caps pageSize at 100

function Stat({ label, value, icon, tone }: { label: string; value: number | string; icon: ReactNode; tone: string }) {
  return (
    <Card className="flex items-center gap-4 p-4">
      <div className={cn('rounded-lg p-2.5', tone)}>{icon}</div>
      <div>
        <p className="text-xs text-zinc-500">{label}</p>
        <p className="text-xl font-semibold tabular-nums">{value}</p>
      </div>
    </Card>
  );
}

/** SuperAdmin global tenant directory: `GET /api/v1/platform/tenants`. */
export function TenantDirectoryPage() {
  const [page, setPage] = useState(1);
  const { data, isLoading, error, refetch, isFetching } = usePlatformTenants(page, PAGE_SIZE);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'' | TenantStatus>('');
  const [planFilter, setPlanFilter] = useState<'' | TenantPlanTier>('');
  const [provisionOpen, setProvisionOpen] = useState(false);
  const [detailsId, setDetailsId] = useState<string | null>(null);
  const [statusTarget, setStatusTarget] = useState<StatusTarget | null>(null);

  const items = useMemo(() => data?.items ?? [], [data]);
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return items.filter(
      (t) =>
        (!q || t.name.toLowerCase().includes(q) || t.subdomain.toLowerCase().includes(q) || t.id.includes(q)) &&
        (!statusFilter || t.status === statusFilter) &&
        (!planFilter || t.planTier === planFilter),
    );
  }, [items, search, statusFilter, planFilter]);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;
  const stats = {
    total: data?.totalCount ?? 0,
    active: items.filter((t) => t.status === 'Active').length,
    suspended: items.filter((t) => t.status === 'Suspended').length,
    dedicated: items.filter((t) => t.isolationStrategy === 'DedicatedDatabase').length,
  };

  return (
    <>
      <PageHeader
        title="Tenant Directory"
        description="Every organization on the platform, its plan, isolation strategy and quotas."
        actions={
          <>
            <Button variant="secondary" icon={<RefreshCw className={cn('h-4 w-4', isFetching && 'animate-spin')} />} onClick={() => void refetch()}>
              Refresh
            </Button>
            <Button icon={<Plus className="h-4 w-4" />} onClick={() => setProvisionOpen(true)}>
              Provision tenant
            </Button>
          </>
        }
      />

      <div className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat label="Total tenants" value={stats.total} icon={<Building2 className="h-5 w-5" />} tone="bg-indigo-50 text-indigo-600 dark:bg-indigo-500/10 dark:text-indigo-300" />
        <Stat label="Active" value={stats.active} icon={<ShieldCheck className="h-5 w-5" />} tone="bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-300" />
        <Stat label="Suspended" value={stats.suspended} icon={<PauseCircle className="h-5 w-5" />} tone="bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-300" />
        <Stat label="Dedicated databases" value={stats.dedicated} icon={<DatabaseZap className="h-5 w-5" />} tone="bg-amber-50 text-amber-600 dark:bg-amber-500/10 dark:text-amber-300" />
      </div>

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b border-zinc-100 p-3 dark:border-zinc-800">
          <div className="relative min-w-52 flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-zinc-400" aria-hidden />
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search by name, subdomain or ID…"
              aria-label="Search tenants"
              className="pl-9"
            />
          </div>
          <Select aria-label="Filter by status" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as '' | TenantStatus)} className="w-auto">
            <option value="">All statuses</option>
            <option value="Active">Active</option>
            <option value="Suspended">Suspended</option>
          </Select>
          <Select aria-label="Filter by plan" value={planFilter} onChange={(e) => setPlanFilter(e.target.value as '' | TenantPlanTier)} className="w-auto">
            <option value="">All plans</option>
            <option value="Free">Free</option>
            <option value="Pro">Pro</option>
            <option value="Enterprise">Enterprise</option>
          </Select>
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
        ) : filtered.length === 0 ? (
          <EmptyState
            icon={<Building2 className="h-5 w-5" />}
            title={items.length ? 'No tenants match your filters' : 'No tenants yet'}
            description={items.length ? 'Try a different search or clear the filters.' : 'Provision the first tenant to get started.'}
          />
        ) : (
          <Table>
            <THead>
              <tr>
                <TH>Tenant</TH>
                <TH>Status</TH>
                <TH>Plan</TH>
                <TH>Isolation</TH>
                <TH>Quotas</TH>
                <TH>Created</TH>
                <TH className="text-right">Actions</TH>
              </tr>
            </THead>
            <TBody>
              {filtered.map((t) => (
                <TR key={t.id} data-testid={`tenant-row-${t.subdomain}`}>
                  <TD>
                    <button type="button" onClick={() => setDetailsId(t.id)} className="group text-left">
                      <span className="block font-medium text-zinc-900 group-hover:text-indigo-600 dark:text-zinc-100 dark:group-hover:text-indigo-400">
                        {t.name}
                      </span>
                      <span className="block font-mono text-xs text-zinc-500">{t.subdomain}</span>
                    </button>
                  </TD>
                  <TD>
                    <StatusBadge status={t.status} />
                  </TD>
                  <TD>
                    <PlanBadge plan={t.planTier} />
                  </TD>
                  <TD>
                    <IsolationBadge strategy={t.isolationStrategy} compact />
                  </TD>
                  <TD className="whitespace-nowrap text-xs text-zinc-600 tabular-nums dark:text-zinc-400">
                    {formatQuota(t.maxAgents)} agents · {formatQuota(t.maxTicketsPerMonth)} tickets/mo · {formatBytes(t.maxStorageBytes)}
                  </TD>
                  <TD className="whitespace-nowrap text-xs text-zinc-500">{formatDate(t.createdAtUtc)}</TD>
                  <TD className="text-right">
                    <div className="flex justify-end gap-1.5">
                      <Button size="sm" variant="ghost" onClick={() => setDetailsId(t.id)}>
                        Details
                      </Button>
                      <Button
                        size="sm"
                        variant={t.status === 'Active' ? 'secondary' : 'success'}
                        icon={t.status === 'Active' ? <PowerOff className="h-3.5 w-3.5 text-rose-500" /> : <Power className="h-3.5 w-3.5" />}
                        onClick={() => setStatusTarget({ id: t.id, name: t.name, subdomain: t.subdomain, status: t.status })}
                        aria-label={`${t.status === 'Active' ? 'Suspend' : 'Activate'} ${t.subdomain}`}
                      >
                        {t.status === 'Active' ? 'Suspend' : 'Activate'}
                      </Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}

        {data && (
          <div className="flex items-center justify-between border-t border-zinc-100 px-4 py-3 text-xs text-zinc-500 dark:border-zinc-800">
            <span>
              Showing {filtered.length} of {data.totalCount} tenants
            </span>
            {totalPages > 1 && (
              <div className="flex items-center gap-1">
                <Button size="icon" variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label="Previous page">
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <span>
                  Page {page} / {totalPages}
                </span>
                <Button size="icon" variant="ghost" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label="Next page">
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            )}
          </div>
        )}
      </Card>

      <ProvisionTenantDialog open={provisionOpen} onClose={() => setProvisionOpen(false)} />
      <TenantDetailsDrawer tenantId={detailsId} onClose={() => setDetailsId(null)} onChangeStatus={setStatusTarget} />
      <StatusChangeDialog target={statusTarget} onClose={() => setStatusTarget(null)} />
    </>
  );
}
