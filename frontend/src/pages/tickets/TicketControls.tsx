import { History, SlidersHorizontal, UserCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { SlaBreachedBadge, SlaCountdown, ticketStatusLabel } from '../../components/domain/TicketBadges';
import { Button, Card, CardBody, CardHeader, Field, Select, Textarea } from '../../components/ui';
import { formatDateTime } from '../../lib/format';
import { useAssignTicket, useUpdateTicketPriority, useUpdateTicketStatus } from '../../lib/tickets';
import {
  TICKET_PRIORITIES,
  TICKET_STATUSES,
  type TenantUser,
  type TicketDetails,
  type TicketPriority,
  type TicketStatus,
} from '../../lib/types';
import { useAuthStore } from '../../stores/auth';
import { toast } from '../../stores/toast';

const NEEDS_NOTES: TicketStatus[] = ['Resolved', 'Closed'];

/** Assign / status / priority (all carrying `rowVersion`), SLA targets and activity log. */
export function TicketControls({ ticket, users }: { ticket: TicketDetails; users: Map<string, TenantUser> }) {
  const me = useAuthStore((s) => s.user)!;
  const assign = useAssignTicket(ticket.id);
  const updateStatus = useUpdateTicketStatus(ticket.id);
  const updatePriority = useUpdateTicketPriority(ticket.id);

  const staff = [...users.values()].filter((u) => (u.role === 'Agent' || u.role === 'TenantAdmin') && u.isActive);
  const [agentId, setAgentId] = useState(ticket.assignedAgentId ?? '');
  const [status, setStatus] = useState<TicketStatus>(ticket.status);
  const [notes, setNotes] = useState(ticket.resolutionNotes ?? '');

  // Follow server-side changes (other agents, hub events) when this agent isn't mid-edit.
  useEffect(() => setAgentId(ticket.assignedAgentId ?? ''), [ticket.assignedAgentId]);
  useEffect(() => setStatus(ticket.status), [ticket.status]);

  const nameOf = (id: string | null) => (id ? (id === me.id ? 'You' : (users.get(id)?.fullName ?? `${id.slice(0, 8)}…`)) : '—');

  const doAssign = (id: string) =>
    assign.mutate(
      { agentId: id, rowVersion: ticket.rowVersion },
      { onSuccess: () => toast.success(`Assigned to ${id === me.id ? 'you' : nameOf(id)}`, ticket.ticketNumber) },
    );

  const doStatus = () =>
    updateStatus.mutate(
      {
        status,
        rowVersion: ticket.rowVersion,
        ...(NEEDS_NOTES.includes(status) && notes.trim() ? { resolutionNotes: notes.trim() } : {}),
      },
      { onSuccess: (t) => toast.success(`Status → ${ticketStatusLabel(t.status)}`, ticket.ticketNumber) },
    );

  const doPriority = (priority: TicketPriority) =>
    updatePriority.mutate(
      { priority, rowVersion: ticket.rowVersion },
      { onSuccess: (t) => toast.success(`Priority → ${t.priority}`, ticket.ticketNumber) },
    );

  const activity = [...ticket.activityLogs].sort((a, b) => b.timestampUtc.localeCompare(a.timestampUtc));

  return (
    <>
      <Card>
        <CardHeader
          title="Ticket controls"
          description={`rowVersion ${ticket.rowVersion} · optimistic concurrency`}
          icon={<SlidersHorizontal className="h-4 w-4" />}
        />
        <CardBody className="space-y-4">
          <div className="space-y-1.5">
            <Field label="Assignee">
              {(id) => (
                <div className="flex gap-2">
                  <Select id={id} value={agentId} onChange={(e) => setAgentId(e.target.value)}>
                    <option value="" disabled>
                      Unassigned
                    </option>
                    {staff.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.fullName} ({u.role === 'TenantAdmin' ? 'Admin' : 'Agent'})
                      </option>
                    ))}
                  </Select>
                  <Button
                    variant="secondary"
                    loading={assign.isPending}
                    disabled={!agentId || agentId === ticket.assignedAgentId}
                    onClick={() => doAssign(agentId)}
                  >
                    Assign
                  </Button>
                </div>
              )}
            </Field>
            {ticket.assignedAgentId !== me.id && (
              <button
                type="button"
                onClick={() => doAssign(me.id)}
                disabled={assign.isPending}
                className="inline-flex items-center gap-1 text-xs font-medium text-indigo-600 hover:underline disabled:opacity-50 dark:text-indigo-400"
              >
                <UserCheck className="h-3.5 w-3.5" aria-hidden /> Assign to me
              </button>
            )}
          </div>

          <div className="space-y-2">
            <Field label="Status">
              {(id) => (
                <div className="flex gap-2">
                  <Select id={id} value={status} onChange={(e) => setStatus(e.target.value as TicketStatus)}>
                    {TICKET_STATUSES.map((s) => (
                      <option key={s} value={s}>
                        {ticketStatusLabel(s)}
                      </option>
                    ))}
                  </Select>
                  <Button variant="secondary" loading={updateStatus.isPending} disabled={status === ticket.status} onClick={doStatus}>
                    Update
                  </Button>
                </div>
              )}
            </Field>
            {NEEDS_NOTES.includes(status) && status !== ticket.status && (
              <Field label="Resolution notes" hint="Optional — visible in the ticket record">
                {(id) => (
                  <Textarea id={id} rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Cleared cached session on auth server." />
                )}
              </Field>
            )}
          </div>

          <Field label="Priority">
            {(id) => (
              <Select
                id={id}
                value={ticket.priority}
                disabled={updatePriority.isPending}
                onChange={(e) => doPriority(e.target.value as TicketPriority)}
              >
                {TICKET_PRIORITIES.map((p) => (
                  <option key={p}>{p}</option>
                ))}
              </Select>
            )}
          </Field>

          <div className="space-y-1.5 border-t border-zinc-100 pt-3 dark:border-zinc-800">
            <p className="text-xs font-medium text-zinc-700 dark:text-zinc-300">SLA targets</p>
            <div className="flex flex-wrap gap-1.5">
              {ticket.isSlaBreached && <SlaBreachedBadge />}
              <SlaCountdown label="First response" dueUtc={ticket.firstResponseDueUtc} metAtUtc={ticket.firstRespondedAtUtc} />
              <SlaCountdown label="Resolution" dueUtc={ticket.resolutionDueUtc} metAtUtc={ticket.resolvedAtUtc ?? ticket.closedAtUtc} />
            </div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-[11px] text-zinc-500">
              <dt>Respond by</dt>
              <dd>{formatDateTime(ticket.firstResponseDueUtc)}</dd>
              <dt>Resolve by</dt>
              <dd>{formatDateTime(ticket.resolutionDueUtc)}</dd>
              {ticket.slaBreachedAtUtc && (
                <>
                  <dt>Breached</dt>
                  <dd className="text-rose-600">{formatDateTime(ticket.slaBreachedAtUtc)}</dd>
                </>
              )}
            </dl>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Activity" icon={<History className="h-4 w-4" />} />
        <CardBody className="p-0">
          {activity.length === 0 ? (
            <p className="px-5 py-4 text-xs text-zinc-500">No activity recorded.</p>
          ) : (
            <ol className="max-h-72 space-y-3 overflow-y-auto px-5 py-4" data-testid="activity-log">
              {activity.map((log) => (
                <li key={log.id} className="relative pl-4 text-xs">
                  <span className="absolute left-0 top-1.5 h-1.5 w-1.5 rounded-full bg-indigo-400" aria-hidden />
                  <p className="text-zinc-800 dark:text-zinc-200">
                    <span className="font-medium">{log.action}</span>
                    {(log.oldValue || log.newValue) && (
                      <span className="text-zinc-500">
                        {' '}
                        {log.oldValue ? `${log.oldValue} → ` : ''}
                        {log.newValue}
                      </span>
                    )}
                  </p>
                  <p className="text-[11px] text-zinc-500">
                    {nameOf(log.performedByUserId)} · {formatDateTime(log.timestampUtc)}
                  </p>
                </li>
              ))}
            </ol>
          )}
        </CardBody>
      </Card>
    </>
  );
}
