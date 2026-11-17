import { useState } from 'react';
import { Power, PowerOff } from 'lucide-react';
import { Button, Dialog, ErrorPanel, Field, Textarea } from '../../components/ui';
import { useUpdateTenantStatus } from '../../lib/queries';
import { toast } from '../../stores/toast';
import type { TenantStatus } from '../../lib/types';

export interface StatusTarget {
  id: string;
  name: string;
  subdomain: string;
  status: TenantStatus;
}

const SUSPEND_REASONS = ['Overdue invoice non-payment', 'Terms of service violation', 'Security incident investigation'];
const ACTIVATE_REASONS = ['Payment received', 'Investigation closed', 'Customer request'];

/**
 * Kill-switch confirmation: `PATCH /api/v1/platform/tenants/{tenantId}/status`.
 * A reason is required client-side (the backend records it in the tenant audit log).
 */
export function StatusChangeDialog({ target, onClose }: { target: StatusTarget | null; onClose: () => void }) {
  const mutation = useUpdateTenantStatus();
  const [reason, setReason] = useState('');
  const next: TenantStatus = target?.status === 'Active' ? 'Suspended' : 'Active';
  const suspending = next === 'Suspended';

  const close = () => {
    setReason('');
    mutation.reset();
    onClose();
  };

  const submit = async () => {
    if (!target || !reason.trim()) return;
    try {
      const res = await mutation.mutateAsync({ tenantId: target.id, status: next, reason: reason.trim() });
      toast.success(
        res.status === 'Suspended' ? `Tenant '${res.subdomain}' suspended` : `Tenant '${res.subdomain}' reactivated`,
        res.status === 'Suspended' ? 'All requests for this tenant are now rejected.' : 'Tenant traffic is flowing again.',
      );
      close();
    } catch {
      /* error rendered inline + toasted by the API client */
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      size="sm"
      title={suspending ? 'Suspend tenant' : 'Reactivate tenant'}
      description={
        target && (
          <>
            {target.name} · <span className="font-mono">{target.subdomain}</span>
          </>
        )
      }
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button
            variant={suspending ? 'danger' : 'success'}
            icon={suspending ? <PowerOff className="h-4 w-4" /> : <Power className="h-4 w-4" />}
            loading={mutation.isPending}
            disabled={!reason.trim()}
            onClick={() => void submit()}
          >
            {suspending ? 'Suspend tenant' : 'Reactivate tenant'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          {suspending
            ? 'Suspension is immediate: every API call scoped to this tenant will be rejected with TENANT_SUSPENDED until reactivated.'
            : 'Reactivation restores access for all users of this tenant immediately.'}
        </p>
        <Field label="Reason" hint="Recorded in the tenant audit log.">
          {(id) => (
            <Textarea
              id={id}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder={suspending ? 'e.g. Overdue invoice non-payment' : 'e.g. Payment received'}
              rows={3}
            />
          )}
        </Field>
        <div className="flex flex-wrap gap-1.5">
          {(suspending ? SUSPEND_REASONS : ACTIVATE_REASONS).map((r) => (
            <button
              key={r}
              type="button"
              onClick={() => setReason(r)}
              className="rounded-full border border-zinc-200 px-2.5 py-1 text-xs text-zinc-600 hover:border-zinc-300 hover:text-zinc-900 dark:border-zinc-700 dark:text-zinc-400 dark:hover:text-zinc-100"
            >
              {r}
            </button>
          ))}
        </div>
        {mutation.error && <ErrorPanel error={mutation.error} />}
      </div>
    </Dialog>
  );
}
