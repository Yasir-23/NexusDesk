import { ArrowRight, Building2, Clock, Database, DatabaseZap, Globe, KeyRound, ShieldCheck, Zap } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { Logo } from '../../components/layout/Logo';
import { RoleBadge } from '../../components/domain/Badges';
import { Button, ErrorPanel, Field, Input, Spinner } from '../../components/ui';
import { ApiProblemError } from '../../lib/api';
import { DEMO_ACCOUNTS, DEMO_PASSWORD, KNOWN_TENANTS, type DemoAccount } from '../../lib/demo';
import { useAuthActions } from '../../lib/useAuthActions';
import { useAuthStore } from '../../stores/auth';
import { CUSTOM, PLATFORM, TenantPicker, resolvePickedTenant } from './TenantPicker';
import { cn } from '../../lib/cn';

function initialTenantChoice(stored: string | null): { value: string; custom: string } {
  if (!stored) return { value: 'acme', custom: '' };
  return KNOWN_TENANTS.some((t) => t.subdomain === stored) ? { value: stored, custom: '' } : { value: CUSTOM, custom: stored };
}

export function LoginPage() {
  const location = useLocation();
  const [params] = useSearchParams();
  const from = (location.state as { from?: string } | null)?.from;
  const { signIn, signInAs } = useAuthActions();
  const storedTenant = useAuthStore((s) => s.tenantSubdomain);

  const initial = initialTenantChoice(storedTenant);
  const [tenantValue, setTenantValue] = useState(initial.value);
  const [customTenant, setCustomTenant] = useState(initial.custom);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [quickPending, setQuickPending] = useState<string | null>(null);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    const tenant = resolvePickedTenant(tenantValue, customTenant);
    if (tenantValue === CUSTOM && !tenant) {
      setError(new ApiProblemError({ status: 400, title: 'Tenant required', detail: 'Enter a tenant subdomain.' }));
      return;
    }
    setSubmitting(true);
    try {
      await signIn(email, password, tenant, from);
    } catch (err) {
      setError(err);
      setSubmitting(false);
    }
  };

  const quickLogin = async (account: DemoAccount) => {
    setError(null);
    setQuickPending(account.email);
    // Mirror the selection into the form so the demo shows what was sent.
    setEmail(account.email);
    setPassword(DEMO_PASSWORD);
    setTenantValue(account.subdomain ?? PLATFORM);
    const ok = await signInAs(account);
    if (!ok) setQuickPending(null);
  };

  const busy = submitting || quickPending !== null;

  return (
    <div className="grid min-h-dvh lg:grid-cols-[1fr_1.1fr]">
      {/* ---------- Form ---------- */}
      <div className="flex flex-col px-6 py-8 sm:px-12">
        <Logo />
        <div className="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
          <h1 className="text-2xl font-semibold tracking-tight">Sign in to NexusDesk</h1>
          <p className="mt-1.5 text-sm text-zinc-500 dark:text-zinc-400">
            Pick a tenant — it is sent as the <code className="font-mono text-xs">X-Tenant-Subdomain</code> header.
          </p>

          {params.get('expired') && (
            <div className="mt-5 flex items-center gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
              <Clock className="h-4 w-4" /> Your session expired. Please sign in again.
            </div>
          )}

          <form onSubmit={onSubmit} className="mt-6 space-y-4" noValidate>
            <div className="space-y-1.5">
              <span className="block text-xs font-medium text-zinc-700 dark:text-zinc-300">Tenant</span>
              <TenantPicker
                idPrefix="login"
                value={tenantValue}
                onChange={setTenantValue}
                customValue={customTenant}
                onCustomChange={setCustomTenant}
                allowPlatform
              />
            </div>
            <Field label="Email">
              {(id) => (
                <Input
                  id={id}
                  type="email"
                  autoComplete="username"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@company.com"
                />
              )}
            </Field>
            <Field label="Password">
              {(id) => (
                <Input
                  id={id}
                  type="password"
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              )}
            </Field>

            {error != null && <ErrorPanel error={error} />}

            <Button type="submit" className="w-full" size="lg" loading={submitting} disabled={busy || !email || !password}>
              Sign in
            </Button>
          </form>

          <p className="mt-6 text-center text-sm text-zinc-500">
            New customer?{' '}
            <Link to="/register" className="font-medium text-indigo-600 hover:text-indigo-500 dark:text-indigo-400">
              Create a portal account
            </Link>
          </p>
        </div>
      </div>

      {/* ---------- Demo quick login ---------- */}
      <div className="relative overflow-hidden bg-zinc-950 px-6 py-10 text-zinc-100 sm:px-12 lg:py-8">
        <div
          aria-hidden
          className="pointer-events-none absolute -right-40 -top-40 h-[480px] w-[480px] rounded-full bg-indigo-600/25 blur-3xl"
        />
        <div aria-hidden className="pointer-events-none absolute -bottom-40 -left-20 h-[380px] w-[380px] rounded-full bg-violet-600/15 blur-3xl" />
        <div className="relative mx-auto flex h-full max-w-xl flex-col justify-center">
          <div className="inline-flex w-fit items-center gap-1.5 rounded-full border border-indigo-400/30 bg-indigo-500/10 px-2.5 py-1 text-xs font-medium text-indigo-200">
            <Zap className="h-3.5 w-3.5" /> One-click demo
          </div>
          <h2 className="mt-4 text-2xl font-semibold tracking-tight text-white">Explore every role and isolation model</h2>
          <p className="mt-2 text-sm text-zinc-400">
            Six seeded accounts across three tenants. Shared-database tenants are isolated by PostgreSQL row-level security;
            Globex runs on its own dedicated database. Password: <code className="font-mono text-zinc-200">{DEMO_PASSWORD}</code>
          </p>

          <ul className="mt-6 grid gap-2.5 sm:grid-cols-2" aria-label="Demo accounts">
            {DEMO_ACCOUNTS.map((a) => {
              const pending = quickPending === a.email;
              const IsoIcon = a.isolation === 'Platform' ? Globe : a.isolation === 'DedicatedDatabase' ? DatabaseZap : Database;
              return (
                <li key={a.email}>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => void quickLogin(a)}
                    aria-label={`Quick login as ${a.email}`}
                    className={cn(
                      'group flex w-full items-start gap-3 rounded-xl border p-3.5 text-left transition-all disabled:cursor-wait',
                      pending
                        ? 'border-indigo-400 bg-indigo-500/15'
                        : 'border-white/10 bg-white/[0.03] hover:border-indigo-400/50 hover:bg-white/[0.06]',
                      busy && !pending && 'opacity-50',
                    )}
                  >
                    <div className="mt-0.5 rounded-lg bg-white/5 p-2 text-zinc-300">
                      {a.role === 'SuperAdmin' ? <ShieldCheck className="h-4 w-4" /> : <Building2 className="h-4 w-4" />}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <RoleBadge role={a.role} />
                        {pending && <Spinner className="h-3.5 w-3.5 text-indigo-300" />}
                      </div>
                      <p className="mt-1.5 truncate text-sm font-medium text-white">{a.email}</p>
                      <p className="mt-0.5 flex items-center gap-1.5 text-xs text-zinc-400">
                        <span className="font-mono">{a.subdomain ?? 'platform'}</span>
                        <span aria-hidden>·</span>
                        {a.planTier}
                        <span aria-hidden>·</span>
                        <IsoIcon className="h-3 w-3" />
                        {a.isolation === 'Platform' ? 'Catalog' : a.isolation === 'DedicatedDatabase' ? 'Dedicated DB' : 'Shared RLS'}
                      </p>
                    </div>
                    <ArrowRight className="mt-1 h-4 w-4 text-zinc-600 transition-transform group-hover:translate-x-0.5 group-hover:text-indigo-300" />
                  </button>
                </li>
              );
            })}
          </ul>

          <div className="mt-6 flex items-center gap-2 text-xs text-zinc-500">
            <KeyRound className="h-3.5 w-3.5" /> JWT bearer auth · refresh via <code className="font-mono">/connect/token</code>
          </div>
        </div>
      </div>
    </div>
  );
}
