import { useState, type ChangeEvent, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { CheckCircle2 } from 'lucide-react';
import { Logo } from '../../components/layout/Logo';
import { Button, Card, ErrorPanel, Field, Input } from '../../components/ui';
import { ApiProblemError, notifyError } from '../../lib/api';
import { registerCustomer } from '../../lib/queries';
import { useAuthActions } from '../../lib/useAuthActions';
import { CUSTOM, TenantPicker, resolvePickedTenant } from './TenantPicker';

/** Public customer self-registration: `POST /api/v1/users/customers/register`. */
export function RegisterPage() {
  const { signIn } = useAuthActions();
  const [tenantValue, setTenantValue] = useState('acme');
  const [customTenant, setCustomTenant] = useState('');
  const [form, setForm] = useState({ fullName: '', email: '', password: '', phoneNumber: '' });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [created, setCreated] = useState<string | null>(null);

  const set = (k: keyof typeof form) => (e: ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    const tenant = resolvePickedTenant(tenantValue, customTenant);
    if (!tenant) {
      setError(new ApiProblemError({ status: 400, title: 'Tenant required', detail: 'Choose the tenant you want to join.' }));
      return;
    }
    if (form.password.length < 8) {
      setError(new ApiProblemError({ status: 400, title: 'Weak password', detail: 'Use at least 8 characters.' }));
      return;
    }
    setSubmitting(true);
    try {
      const res = await registerCustomer(tenant, {
        email: form.email.trim(),
        fullName: form.fullName.trim(),
        password: form.password,
        phoneNumber: form.phoneNumber.trim() || undefined,
      });
      setCreated(res.email);
      // Straight into the portal with the new credentials.
      try {
        await signIn(form.email, form.password, tenant);
      } catch (loginErr) {
        if (loginErr instanceof ApiProblemError) notifyError(loginErr);
        setSubmitting(false);
      }
    } catch (err) {
      setError(err);
      setSubmitting(false);
    }
  };

  return (
    <div className="flex min-h-dvh flex-col items-center px-4 py-10">
      <Logo />
      <Card className="mt-8 w-full max-w-md p-6 sm:p-8">
        <h1 className="text-xl font-semibold tracking-tight">Create your support account</h1>
        <p className="mt-1 text-sm text-zinc-500">Register as a customer of a NexusDesk tenant to open tickets and chat with support.</p>

        {created ? (
          <div className="mt-6 flex items-start gap-3 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-200">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            <div>
              Account <strong>{created}</strong> created. Signing you in…{' '}
              <Link to="/login" className="underline">
                Go to sign in
              </Link>
            </div>
          </div>
        ) : (
          <form onSubmit={onSubmit} className="mt-6 space-y-4" noValidate>
            <div className="space-y-1.5">
              <span className="block text-xs font-medium text-zinc-700 dark:text-zinc-300">Tenant</span>
              <TenantPicker
                idPrefix="register"
                value={tenantValue}
                onChange={setTenantValue}
                customValue={customTenant}
                onCustomChange={setCustomTenant}
              />
              {tenantValue === CUSTOM && <p className="text-xs text-zinc-500">Sent as X-Tenant-Subdomain.</p>}
            </div>
            <Field label="Full name">
              {(id) => <Input id={id} required autoComplete="name" value={form.fullName} onChange={set('fullName')} placeholder="Jane Doe" />}
            </Field>
            <Field label="Email">
              {(id) => (
                <Input id={id} type="email" required autoComplete="email" value={form.email} onChange={set('email')} placeholder="jane@example.com" />
              )}
            </Field>
            <Field label="Password" hint="At least 8 characters.">
              {(id) => <Input id={id} type="password" required autoComplete="new-password" value={form.password} onChange={set('password')} />}
            </Field>
            <Field label="Phone (optional)">
              {(id) => <Input id={id} type="tel" autoComplete="tel" value={form.phoneNumber} onChange={set('phoneNumber')} placeholder="+1-555-0144" />}
            </Field>

            {error != null && <ErrorPanel error={error} />}

            <Button
              type="submit"
              size="lg"
              className="w-full"
              loading={submitting}
              disabled={!form.fullName || !form.email || !form.password}
            >
              Create account
            </Button>
          </form>
        )}
      </Card>
      <p className="mt-6 text-sm text-zinc-500">
        Already have an account?{' '}
        <Link to="/login" className="font-medium text-indigo-600 hover:text-indigo-500 dark:text-indigo-400">
          Sign in
        </Link>
      </p>
    </div>
  );
}
