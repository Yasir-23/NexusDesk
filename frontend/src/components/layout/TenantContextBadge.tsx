import { Building2, ChevronDown, Globe, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { IsolationBadge, PlanBadge, StatusBadge } from '../domain/Badges';
import { Input, Spinner } from '../ui';
import { MenuItem, MenuLabel, Popover } from '../ui/Popover';
import { KNOWN_TENANTS } from '../../lib/demo';
import { useTenantCurrent } from '../../lib/queries';
import { useAuthStore } from '../../stores/auth';
import { cn } from '../../lib/cn';

/**
 * Live tenant & isolation badge driven by `GET /api/v1/tenant/current`.
 * SuperAdmins get a tenant switcher that only changes the `X-Tenant-Subdomain` header.
 */
export function TenantContextBadge() {
  const role = useAuthStore((s) => s.user?.role);
  const subdomain = useAuthStore((s) => s.tenantSubdomain);
  const setTenant = useAuthStore((s) => s.setTenantSubdomain);
  const { data, isLoading, isError, error, refetch, isFetching } = useTenantCurrent();
  const [custom, setCustom] = useState('');
  const canSwitch = role === 'SuperAdmin';

  const ctx = data?.tenantContext;

  const chip = (
    <span className="flex min-w-0 items-center gap-2">
      {subdomain ? (
        <Building2 className="h-4 w-4 shrink-0 text-zinc-400" aria-hidden />
      ) : (
        <Globe className="h-4 w-4 shrink-0 text-sky-500" aria-hidden />
      )}
      <span className="truncate font-mono text-xs font-medium" data-testid="active-tenant">
        {subdomain ?? 'platform'}
      </span>
      {subdomain && isLoading && <Spinner className="h-3.5 w-3.5" />}
      {ctx && (
        <span className="hidden items-center gap-1.5 md:flex">
          <PlanBadge plan={ctx.planTier} />
          <IsolationBadge strategy={ctx.isolationStrategy} compact />
          <span className="hidden font-mono text-[11px] text-zinc-500 lg:inline" title="Active physical database">
            db:{data.activeDatabase}
          </span>
          {ctx.status !== 'Active' && <StatusBadge status={ctx.status} />}
        </span>
      )}
      {!subdomain && <IsolationBadge strategy="Platform" />}
      {subdomain && isError && <span className="text-xs text-rose-500">unresolved</span>}
    </span>
  );

  return (
    <Popover
      align="left"
      className="w-80"
      trigger={({ toggle, open }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label="Tenant context"
          className={cn(
            'flex h-9 min-w-0 items-center gap-2 rounded-lg border px-2.5 text-zinc-800 transition-colors dark:text-zinc-200',
            open
              ? 'border-indigo-300 bg-indigo-50/50 dark:border-indigo-500/40 dark:bg-indigo-500/10'
              : 'border-zinc-200 bg-white hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900 dark:hover:bg-zinc-800',
          )}
        >
          {chip}
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-zinc-400" aria-hidden />
        </button>
      )}
    >
      {(close) => (
        <div className="text-sm">
          <div className="px-2.5 py-2">
            <div className="flex items-center justify-between">
              <p className="text-xs font-semibold uppercase tracking-wider text-zinc-400">Request context</p>
              {subdomain && (
                <button
                  type="button"
                  onClick={() => void refetch()}
                  className="rounded p-1 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
                  aria-label="Refresh tenant context"
                >
                  <RefreshCw className={cn('h-3.5 w-3.5', isFetching && 'animate-spin')} />
                </button>
              )}
            </div>
            {!subdomain ? (
              <p className="mt-1.5 text-xs text-zinc-500">
                Platform scope — no <code className="font-mono">X-Tenant-Subdomain</code> header is sent.
              </p>
            ) : isError ? (
              <p className="mt-1.5 text-xs text-rose-600 dark:text-rose-400">
                {(error as Error)?.message ?? 'Tenant could not be resolved.'}
              </p>
            ) : ctx ? (
              <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5 text-xs">
                <dt className="text-zinc-500">Header</dt>
                <dd className="font-mono">X-Tenant-Subdomain: {subdomain}</dd>
                <dt className="text-zinc-500">Status</dt>
                <dd><StatusBadge status={ctx.status} /></dd>
                <dt className="text-zinc-500">Plan</dt>
                <dd><PlanBadge plan={ctx.planTier} /></dd>
                <dt className="text-zinc-500">Isolation</dt>
                <dd><IsolationBadge strategy={ctx.isolationStrategy} /></dd>
                <dt className="text-zinc-500">Database</dt>
                <dd className="font-mono">{data.activeDatabase}</dd>
                <dt className="text-zinc-500">RLS tenant</dt>
                <dd className="truncate font-mono" title={data.rlsSessionTenantId ?? ''}>
                  {data.rlsSessionTenantId ?? '—'}
                </dd>
                <dt className="text-zinc-500">Members</dt>
                <dd>{data.membershipsCount}</dd>
              </dl>
            ) : (
              <Spinner className="mt-2" />
            )}
          </div>

          {canSwitch && (
            <div role="group" aria-label="Switch tenant scope" className="mt-1 border-t border-zinc-100 pt-1 dark:border-zinc-800">
              <MenuLabel>Switch tenant scope</MenuLabel>
              <MenuItem
                active={!subdomain}
                onClick={() => {
                  setTenant(null);
                  close();
                }}
              >
                <Globe className="h-4 w-4 text-sky-500" /> Platform (no tenant)
              </MenuItem>
              {KNOWN_TENANTS.map((t) => (
                <MenuItem
                  key={t.subdomain}
                  active={subdomain === t.subdomain}
                  onClick={() => {
                    setTenant(t.subdomain);
                    close();
                  }}
                >
                  <Building2 className="h-4 w-4 text-zinc-400" />
                  <span className="flex-1">{t.name}</span>
                  <span className="font-mono text-xs text-zinc-400">{t.subdomain}</span>
                </MenuItem>
              ))}
              <form
                className="flex gap-1.5 px-1.5 pb-1.5 pt-1"
                onSubmit={(e) => {
                  e.preventDefault();
                  if (!custom.trim()) return;
                  setTenant(custom);
                  setCustom('');
                  close();
                }}
              >
                <Input
                  value={custom}
                  onChange={(e) => setCustom(e.target.value)}
                  placeholder="other subdomain…"
                  aria-label="Custom tenant subdomain"
                  className="h-8 font-mono text-xs"
                />
              </form>
            </div>
          )}
        </div>
      )}
    </Popover>
  );
}
