import { ArrowUpCircle, ShieldAlert } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Button, Dialog, ErrorPanel, Field, Input } from '../../components/ui';
import { ApiProblemError } from '../../lib/api';
import { PLAN_AGENT_LIMIT_EXCEEDED, useCreateAgent } from '../../lib/billing';
import { toast } from '../../stores/toast';

/** `POST /api/v1/users/agents`, surfacing the 422 plan-quota error with an upgrade hint. */
export function AddAgentDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const create = useCreateAgent();
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('Password123!');
  const [phone, setPhone] = useState('');

  const close = () => {
    setFullName('');
    setEmail('');
    setPassword('Password123!');
    setPhone('');
    create.reset();
    onClose();
  };

  const quotaError = create.error instanceof ApiProblemError && create.error.code === PLAN_AGENT_LIMIT_EXCEEDED ? create.error : null;
  const canSubmit = fullName.trim() && email.trim() && password.length >= 8;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    try {
      const agent = await create.mutateAsync({
        fullName: fullName.trim(),
        email: email.trim(),
        password,
        ...(phone.trim() ? { phoneNumber: phone.trim() } : {}),
      });
      toast.success('Agent added', `${agent.fullName} · ${agent.email}`);
      close();
    } catch {
      /* inline */
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      title="Add agent"
      description="Creates an Agent account in this tenant. Each active agent uses one plan seat."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form="add-agent-form" loading={create.isPending} disabled={!canSubmit}>
            Add agent
          </Button>
        </>
      }
    >
      <form id="add-agent-form" onSubmit={submit} className="space-y-4">
        {quotaError ? (
          <div role="alert" data-testid="agent-quota-error" className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm dark:border-amber-500/40 dark:bg-amber-500/10">
            <p className="flex items-center gap-2 font-semibold text-amber-900 dark:text-amber-200">
              <ShieldAlert className="h-4 w-4" aria-hidden /> Agent seat limit reached
            </p>
            <p className="mt-1 text-amber-800 dark:text-amber-300">{quotaError.userMessage}</p>
            <p className="mt-2 flex items-center gap-1.5 text-xs text-amber-800 dark:text-amber-300">
              <ArrowUpCircle className="h-3.5 w-3.5" aria-hidden /> Upgrade the plan in the Plans section below to add more agents.
            </p>
            <p className="mt-1 font-mono text-[11px] text-amber-700/80">
              {quotaError.code} · HTTP {quotaError.status}
            </p>
          </div>
        ) : (
          create.error && <ErrorPanel error={create.error} />
        )}
        <Field label="Full name">
          {(id) => <Input id={id} value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="Sarah Connor" required />}
        </Field>
        <Field label="Email">
          {(id) => <Input id={id} type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="sarah.agent@acme.com" required />}
        </Field>
        <Field label="Initial password" hint="At least 8 characters. Share it with the agent securely.">
          {(id) => <Input id={id} type="text" value={password} onChange={(e) => setPassword(e.target.value)} required />}
        </Field>
        <Field label="Phone (optional)">
          {(id) => <Input id={id} type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+1-800-555-0299" />}
        </Field>
      </form>
    </Dialog>
  );
}
