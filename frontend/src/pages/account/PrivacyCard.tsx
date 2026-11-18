import { AlertTriangle, CheckCircle2, ShieldOff } from 'lucide-react';
import { useState } from 'react';
import { Button, Card, CardBody, CardHeader, Dialog, ErrorPanel, Field, Input } from '../../components/ui';
import { formatDateTime } from '../../lib/format';
import { useForgetMe } from '../../lib/notifications';
import { useAuthActions } from '../../lib/useAuthActions';
import { toast } from '../../stores/toast';

export const ERASE_CONFIRMATION = 'ERASE';

/**
 * GDPR right to erasure (`POST /api/v1/privacy/forget-me`). Not rendered for SuperAdmin,
 * which the backend protects from self-erasure.
 */
export function PrivacyCard({ email }: { email: string }) {
  const [open, setOpen] = useState(false);
  return (
    <Card className="border-rose-200 lg:col-span-2 dark:border-rose-500/30" data-testid="gdpr-danger-zone">
      <CardHeader
        title="Privacy & GDPR Data Erasure"
        icon={<ShieldOff className="h-4 w-4 text-rose-500" />}
        description="Danger zone · Article 17 right to erasure"
      />
      <CardBody className="flex flex-wrap items-center justify-between gap-4">
        <p className="max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
          Permanently anonymize your personal data (name, email, phone) in this workspace and revoke all of your sessions. Ticket history is kept
          for the organization but is no longer linked to you. <strong className="text-zinc-900 dark:text-zinc-100">This cannot be undone.</strong>
        </p>
        <Button variant="danger" onClick={() => setOpen(true)}>
          Erase my data
        </Button>
      </CardBody>
      <ForgetMeDialog open={open} onClose={() => setOpen(false)} email={email} />
    </Card>
  );
}

function ForgetMeDialog({ open, onClose, email }: { open: boolean; onClose: () => void; email: string }) {
  const forget = useForgetMe();
  const { signOut } = useAuthActions();
  const [typed, setTyped] = useState('');
  const done = forget.data;

  const finish = () => {
    if (done) toast.success('Your personal data has been erased', `Anonymized at ${formatDateTime(done.anonymizedAtUtc)}`);
    signOut();
  };

  const close = () => {
    // Once erased, the session is revoked server-side: any way out of the dialog signs out.
    if (done) return finish();
    setTyped('');
    forget.reset();
    onClose();
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      size="sm"
      title={done ? 'Data erased' : 'Erase your personal data?'}
      footer={
        done ? (
          <Button onClick={finish}>Sign out</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={close}>
              Cancel
            </Button>
            <Button
              variant="danger"
              loading={forget.isPending}
              disabled={typed !== ERASE_CONFIRMATION}
              onClick={() => forget.mutate()}
            >
              Permanently erase
            </Button>
          </>
        )
      }
    >
      {done ? (
        <div className="space-y-3 text-sm" data-testid="erasure-confirmation">
          <p className="flex items-center gap-2 font-medium text-emerald-700 dark:text-emerald-400">
            <CheckCircle2 className="h-5 w-5" /> Your account has been anonymized.
          </p>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 rounded-lg bg-zinc-50 p-3 dark:bg-zinc-800/60">
            <dt className="text-zinc-500">Anonymized at</dt>
            <dd className="font-medium" data-testid="anonymized-at">
              {formatDateTime(done.anonymizedAtUtc)}
            </dd>
            <dt className="text-zinc-500">User ID</dt>
            <dd className="break-all font-mono text-xs">{done.userId}</dd>
          </dl>
          <p className="text-zinc-500">All your sessions were revoked. You'll be signed out now.</p>
        </div>
      ) : (
        <div className="space-y-4 text-sm">
          {forget.error && <ErrorPanel error={forget.error} />}
          <p className="flex gap-2 rounded-lg bg-rose-50 p-3 text-rose-800 dark:bg-rose-500/10 dark:text-rose-200">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <span>
              <strong>{email}</strong> will be anonymized and signed out everywhere. You will not be able to sign in with this account again.
            </span>
          </p>
          <Field label={`Type ${ERASE_CONFIRMATION} to confirm`}>
            {(id) => <Input id={id} value={typed} onChange={(e) => setTyped(e.target.value)} autoComplete="off" />}
          </Field>
        </div>
      )}
    </Dialog>
  );
}
