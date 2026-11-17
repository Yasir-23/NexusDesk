import { useState, type ChangeEvent, type FormEvent } from 'react';
import { CheckCircle2, Copy, DatabaseZap, Rocket } from 'lucide-react';
import { IsolationBadge, PlanBadge } from '../../components/domain/Badges';
import { Button, Dialog, ErrorPanel, Field, Input } from '../../components/ui';
import { DEMO_PASSWORD } from '../../lib/demo';
import { formatBytes, formatNumber } from '../../lib/format';
import { useProvisionTenant } from '../../lib/queries';
import { toast } from '../../stores/toast';
import { cn } from '../../lib/cn';
import type { ProvisionTenantRequest, ProvisionTenantResponse, TenantPlanTier } from '../../lib/types';

const PLANS: { tier: TenantPlanTier; price: string; blurb: string }[] = [
  { tier: 'Free', price: '$0', blurb: 'Shared DB · RLS' },
  { tier: 'Pro', price: '$49/mo', blurb: 'Shared DB · RLS' },
  { tier: 'Enterprise', price: '$199/mo', blurb: 'Dedicated database' },
];

const empty: ProvisionTenantRequest = {
  organizationName: '',
  subdomain: '',
  adminEmail: '',
  adminFullName: '',
  planTier: 'Pro',
  adminPassword: DEMO_PASSWORD,
};

function slugify(name: string) {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 30);
}

/** `POST /api/v1/platform/tenants` — provisions a tenant and its admin account. */
export function ProvisionTenantDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const mutation = useProvisionTenant();
  const [form, setForm] = useState<ProvisionTenantRequest>(empty);
  const [subdomainTouched, setSubdomainTouched] = useState(false);
  const [result, setResult] = useState<ProvisionTenantResponse | null>(null);

  const close = () => {
    setForm(empty);
    setSubdomainTouched(false);
    setResult(null);
    mutation.reset();
    onClose();
  };

  const set = (k: keyof ProvisionTenantRequest) => (e: ChangeEvent<HTMLInputElement>) => {
    const value = e.target.value;
    setForm((f) => {
      const next = { ...f, [k]: value };
      if (k === 'organizationName' && !subdomainTouched) next.subdomain = slugify(value);
      return next;
    });
  };

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const res = await mutation.mutateAsync({
        ...form,
        organizationName: form.organizationName.trim(),
        subdomain: form.subdomain.trim(),
        adminEmail: form.adminEmail.trim(),
        adminFullName: form.adminFullName.trim(),
      });
      setResult(res);
      toast.success(`Tenant '${res.subdomain}' provisioned`, `${res.planTier} · ${res.isolationStrategy}`);
    } catch {
      /* rendered inline */
    }
  };

  const valid =
    form.organizationName.trim() && form.subdomain.trim() && form.adminEmail.trim() && form.adminFullName.trim() && form.adminPassword.length >= 8;

  return (
    <Dialog
      open={open}
      onClose={close}
      size="lg"
      title={result ? 'Tenant provisioned' : 'Provision new tenant'}
      description={result ? undefined : 'Creates the tenant, applies plan quotas, picks an isolation strategy, and seeds its admin.'}
      footer={
        result ? (
          <Button onClick={close}>Done</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={close}>
              Cancel
            </Button>
            <Button
              type="submit"
              form="provision-tenant-form"
              icon={<Rocket className="h-4 w-4" />}
              loading={mutation.isPending}
              disabled={!valid}
            >
              Provision tenant
            </Button>
          </>
        )
      }
    >
      {result ? (
        <div className="space-y-4">
          <div className="flex items-start gap-3 rounded-lg border border-emerald-200 bg-emerald-50 p-4 dark:border-emerald-500/30 dark:bg-emerald-500/10">
            <CheckCircle2 className="mt-0.5 h-5 w-5 text-emerald-600" />
            <div className="text-sm">
              <p className="font-medium text-emerald-900 dark:text-emerald-100">{result.organizationName} is live.</p>
              <p className="mt-0.5 text-emerald-800 dark:text-emerald-200">
                Sign in with tenant <code className="font-mono">{result.subdomain}</code> and the admin credentials you entered.
              </p>
            </div>
          </div>
          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2.5 text-sm">
            <dt className="text-zinc-500">Tenant ID</dt>
            <dd className="font-mono text-xs">{result.tenantId}</dd>
            <dt className="text-zinc-500">Portal URL</dt>
            <dd className="flex items-center gap-2">
              <span className="font-mono text-xs">{result.portalUrl}</span>
              <button
                type="button"
                aria-label="Copy portal URL"
                className="text-zinc-400 hover:text-zinc-700"
                onClick={() => void navigator.clipboard?.writeText(result.portalUrl)}
              >
                <Copy className="h-3.5 w-3.5" />
              </button>
            </dd>
            <dt className="text-zinc-500">Plan</dt>
            <dd><PlanBadge plan={result.planTier} /></dd>
            <dt className="text-zinc-500">Isolation</dt>
            <dd><IsolationBadge strategy={result.isolationStrategy} /></dd>
            <dt className="text-zinc-500">Quotas</dt>
            <dd>
              {formatNumber(result.planLimits.maxAgents)} agents · {formatNumber(result.planLimits.maxTicketsPerMonth)} tickets/mo ·{' '}
              {formatBytes(result.planLimits.maxStorageBytes)}
            </dd>
          </dl>
        </div>
      ) : (
        <form id="provision-tenant-form" onSubmit={onSubmit} className="space-y-5" noValidate>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Organization name">
              {(id) => <Input id={id} value={form.organizationName} onChange={set('organizationName')} placeholder="Delta Logistics" />}
            </Field>
            <Field label="Subdomain" hint="Used as X-Tenant-Subdomain.">
              {(id) => (
                <Input
                  id={id}
                  className="font-mono"
                  value={form.subdomain}
                  onChange={(e) => {
                    setSubdomainTouched(true);
                    setForm((f) => ({ ...f, subdomain: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, '') }));
                  }}
                  placeholder="delta"
                />
              )}
            </Field>
          </div>

          <div>
            <span className="mb-1.5 block text-xs font-medium text-zinc-700 dark:text-zinc-300">Plan tier</span>
            <div role="radiogroup" aria-label="Plan tier" className="grid gap-2 sm:grid-cols-3">
              {PLANS.map((p) => {
                const active = form.planTier === p.tier;
                return (
                  <button
                    key={p.tier}
                    type="button"
                    role="radio"
                    aria-checked={active}
                    onClick={() => setForm((f) => ({ ...f, planTier: p.tier }))}
                    className={cn(
                      'rounded-lg border p-3 text-left transition-colors',
                      active
                        ? 'border-indigo-500 bg-indigo-50/60 ring-1 ring-indigo-500 dark:bg-indigo-500/10'
                        : 'border-zinc-200 hover:border-zinc-300 dark:border-zinc-700 dark:hover:border-zinc-600',
                    )}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-semibold">{p.tier}</span>
                      <span className="text-xs text-zinc-500">{p.price}</span>
                    </div>
                    <p className="mt-1 flex items-center gap-1 text-xs text-zinc-500">
                      {p.tier === 'Enterprise' && <DatabaseZap className="h-3 w-3 text-amber-500" />}
                      {p.blurb}
                    </p>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Admin full name">
              {(id) => <Input id={id} value={form.adminFullName} onChange={set('adminFullName')} placeholder="Delta Admin" />}
            </Field>
            <Field label="Admin email">
              {(id) => <Input id={id} type="email" value={form.adminEmail} onChange={set('adminEmail')} placeholder="admin@delta.com" />}
            </Field>
          </div>
          <Field label="Admin password" hint="Prefilled with the demo password; at least 8 characters.">
            {(id) => <Input id={id} type="text" className="font-mono" value={form.adminPassword} onChange={set('adminPassword')} />}
          </Field>

          {mutation.error && <ErrorPanel error={mutation.error} />}
        </form>
      )}
    </Dialog>
  );
}
