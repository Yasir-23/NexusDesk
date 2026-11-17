import { Database, DatabaseZap, KeyRound, Network, Settings2, ShieldCheck, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { PageHeader } from '../components/layout/AppShell';
import { IsolationBadge, PlanBadge, RoleBadge, StatusBadge } from '../components/domain/Badges';
import { Badge, Card, CardBody, CardHeader, ErrorPanel, Skeleton, Table, TBody, TD, TH, THead, TR } from '../components/ui';
import { navFor } from '../lib/navigation';
import { useTenantCurrent } from '../lib/queries';
import { useAuthStore } from '../stores/auth';

/**
 * Tenant overview for staff. Shows the live tenant context resolved by the backend
 * (`GET /api/v1/tenant/current`) so the isolation model is visible in the demo.
 */
export function DashboardPage() {
  const user = useAuthStore((s) => s.user)!;
  const { data, isLoading, error, refetch } = useTenantCurrent();
  const ctx = data?.tenantContext;
  const dedicated = ctx?.isolationStrategy === 'DedicatedDatabase';
  const upcoming = navFor(user.role)
    .flatMap((s) => s.items)
    .filter((i) => i.milestone);

  return (
    <>
      <PageHeader
        title={`Welcome back, ${user.fullName.split(' ')[0]}`}
        description="Your tenant's live request context, isolation boundary and team."
      />

      {error ? (
        <ErrorPanel error={error} onRetry={() => void refetch()} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-3">
          <Card className="lg:col-span-2">
            <CardHeader
              title="Tenant isolation"
              description="Resolved server-side from the X-Tenant-Subdomain header on every request"
              icon={dedicated ? <DatabaseZap className="h-4 w-4" /> : <ShieldCheck className="h-4 w-4" />}
              actions={ctx && <StatusBadge status={ctx.status} />}
            />
            <CardBody>
              {isLoading || !data || !ctx ? (
                <Skeleton className="h-40 w-full" />
              ) : (
                <div className="grid gap-5 sm:grid-cols-2">
                  <div className="space-y-4">
                    <div>
                      <p className="text-xs uppercase tracking-wide text-zinc-500">Tenant</p>
                      <p className="mt-1 font-mono text-lg font-semibold">{ctx.subdomain}</p>
                      <div className="mt-2 flex flex-wrap gap-1.5">
                        <PlanBadge plan={ctx.planTier} />
                        <IsolationBadge strategy={ctx.isolationStrategy} />
                        {ctx.isResolved && <Badge tone="green">Resolved</Badge>}
                      </div>
                    </div>
                    <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
                      <dt className="text-zinc-500">Tenant ID</dt>
                      <dd className="truncate font-mono text-xs" title={ctx.tenantId}>{ctx.tenantId}</dd>
                      <dt className="text-zinc-500">RLS session</dt>
                      <dd className="truncate font-mono text-xs" title={data.rlsSessionTenantId ?? ''}>
                        {data.rlsSessionTenantId ?? '—'}
                      </dd>
                    </dl>
                  </div>
                  <div
                    className={
                      dedicated
                        ? 'rounded-xl border border-amber-200 bg-amber-50/60 p-4 dark:border-amber-500/30 dark:bg-amber-500/5'
                        : 'rounded-xl border border-teal-200 bg-teal-50/60 p-4 dark:border-teal-500/30 dark:bg-teal-500/5'
                    }
                  >
                    <div className="flex items-center gap-2 text-sm font-medium">
                      <Database className="h-4 w-4" /> Active database
                    </div>
                    <p className="mt-2 font-mono text-base font-semibold">{data.activeDatabase}</p>
                    <p className="mt-2 text-xs text-zinc-600 dark:text-zinc-400">
                      {dedicated
                        ? 'Enterprise isolation: this tenant’s connections are routed to its own physical PostgreSQL database.'
                        : 'Shared database: every query is fenced by PostgreSQL row-level security bound to the session tenant ID.'}
                    </p>
                  </div>
                </div>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Signed in as" icon={<KeyRound className="h-4 w-4" />} />
            <CardBody className="space-y-3 text-sm">
              <div>
                <p className="font-medium">{user.fullName}</p>
                <p className="text-zinc-500">{user.email}</p>
              </div>
              <RoleBadge role={user.role} />
              <Link to="/account" className="block text-sm font-medium text-indigo-600 hover:text-indigo-500 dark:text-indigo-400">
                View permissions →
              </Link>
            </CardBody>
          </Card>

          <Card className="lg:col-span-2">
            <CardHeader
              title="Team members"
              description={data ? `${data.membershipsCount} memberships in this tenant` : undefined}
              icon={<Users className="h-4 w-4" />}
            />
            {data ? (
              <Table>
                <THead>
                  <tr>
                    <TH>Name</TH>
                    <TH>Email</TH>
                    <TH>Role</TH>
                  </tr>
                </THead>
                <TBody>
                  {data.memberships.map((m) => (
                    <TR key={m.id}>
                      <TD className="font-medium">{m.fullName}</TD>
                      <TD className="text-zinc-500">{m.email}</TD>
                      <TD>
                        <RoleBadge role={m.role} />
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            ) : (
              <CardBody>
                <Skeleton className="h-24 w-full" />
              </CardBody>
            )}
          </Card>

          <Card>
            <CardHeader title="Coming next" description="Wired routes, implemented in upcoming milestones" icon={<Network className="h-4 w-4" />} />
            <CardBody className="space-y-1.5 p-3">
              {upcoming.map((i) => (
                <Link
                  key={i.to}
                  to={i.to}
                  className="flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-zinc-700 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-800"
                >
                  <i.icon className="h-4 w-4 text-zinc-400" />
                  <span className="flex-1">{i.label}</span>
                  <span className="text-[10px] font-medium text-zinc-400">M{i.milestone}</span>
                </Link>
              ))}
              {data && (
                <p className="flex items-center gap-1.5 px-2.5 pt-2 text-xs text-zinc-400">
                  <Settings2 className="h-3.5 w-3.5" /> {data.settingsCount} tenant settings loaded
                </p>
              )}
            </CardBody>
          </Card>
        </div>
      )}
    </>
  );
}
