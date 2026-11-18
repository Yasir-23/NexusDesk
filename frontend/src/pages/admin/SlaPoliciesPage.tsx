import { CheckCircle2, Save, Timer } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import { PriorityBadge, formatDuration } from '../../components/domain/TicketBadges';
import { Badge, Button, Card, CardBody, CardHeader, ErrorPanel, Field, Input, Select, Skeleton, Table, TBody, TD, TH, THead, TR } from '../../components/ui';
import { useSlaPolicies, useUpsertSlaPolicy } from '../../lib/admin';
import { formatRelative } from '../../lib/format';
import { TICKET_PRIORITIES, type SlaPolicy, type TicketPriority } from '../../lib/types';
import { toast } from '../../stores/toast';

/** Suggested targets used when a priority has no policy yet. */
const DEFAULTS: Record<TicketPriority, { first: number; resolution: number }> = {
  Low: { first: 480, resolution: 4320 },
  Medium: { first: 240, resolution: 1440 },
  High: { first: 60, resolution: 480 },
  Urgent: { first: 15, resolution: 120 },
};

const minutes = (m: number) => formatDuration(m * 60_000);

/** `GET` + `PUT /api/v1/tenant-admin/sla-policies`. */
export function SlaPoliciesPage() {
  const { data, isLoading, error, refetch } = useSlaPolicies();
  const byPriority = new Map((data ?? []).map((p) => [p.priority, p]));
  const [selected, setSelected] = useState<TicketPriority>('Urgent');
  const [justSaved, setJustSaved] = useState<TicketPriority | null>(null);

  return (
    <>
      <PageHeader
        title="SLA Policies"
        description="First-response and resolution targets per priority. New tickets get their due dates from these policies."
      />
      {error ? (
        <ErrorPanel error={error} onRetry={() => void refetch()} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
          <Card>
            <CardHeader title="Policies by priority" icon={<Timer className="h-4 w-4" />} description="Click a row to edit it." />
            {isLoading ? (
              <CardBody>
                <Skeleton className="h-40 w-full" />
              </CardBody>
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>Priority</TH>
                    <TH>First response</TH>
                    <TH>Resolution</TH>
                    <TH>Last updated</TH>
                  </tr>
                </THead>
                <TBody>
                  {TICKET_PRIORITIES.map((priority) => {
                    const p = byPriority.get(priority);
                    return (
                      <TR
                        key={priority}
                        data-testid={`sla-row-${priority}`}
                        onClick={() => setSelected(priority)}
                        className={selected === priority ? 'cursor-pointer bg-indigo-50/60 dark:bg-indigo-500/10' : 'cursor-pointer'}
                      >
                        <TD>
                          <PriorityBadge priority={priority} />
                        </TD>
                        {p ? (
                          <>
                            <TD className="tabular-nums">
                              {minutes(p.firstResponseTargetMinutes)} <span className="text-xs text-zinc-500">({p.firstResponseTargetMinutes} min)</span>
                            </TD>
                            <TD className="tabular-nums">
                              {minutes(p.resolutionTargetMinutes)} <span className="text-xs text-zinc-500">({p.resolutionTargetMinutes} min)</span>
                            </TD>
                            <TD className="text-zinc-500">
                              {justSaved === priority ? (
                                <Badge tone="green" icon={<CheckCircle2 className="h-3 w-3" />}>
                                  Saved
                                </Badge>
                              ) : (
                                formatRelative(p.updatedAtUtc ?? p.createdAtUtc)
                              )}
                            </TD>
                          </>
                        ) : (
                          <TD colSpan={3}>
                            <Badge tone="neutral">Not configured</Badge>
                          </TD>
                        )}
                      </TR>
                    );
                  })}
                </TBody>
              </Table>
            )}
          </Card>
          <SlaEditor
            key={selected}
            priority={selected}
            onPriorityChange={setSelected}
            existing={byPriority.get(selected)}
            onSaved={(p) => {
              setJustSaved(p);
              setTimeout(() => setJustSaved((cur) => (cur === p ? null : cur)), 4000);
            }}
          />
        </div>
      )}
    </>
  );
}

function SlaEditor({
  priority,
  onPriorityChange,
  existing,
  onSaved,
}: {
  priority: TicketPriority;
  onPriorityChange: (p: TicketPriority) => void;
  existing: SlaPolicy | undefined;
  onSaved: (p: TicketPriority) => void;
}) {
  const upsert = useUpsertSlaPolicy();
  const [first, setFirst] = useState(String(existing?.firstResponseTargetMinutes ?? DEFAULTS[priority].first));
  const [resolution, setResolution] = useState(String(existing?.resolutionTargetMinutes ?? DEFAULTS[priority].resolution));

  // The list may load after the editor mounts.
  useEffect(() => {
    if (existing) {
      setFirst(String(existing.firstResponseTargetMinutes));
      setResolution(String(existing.resolutionTargetMinutes));
    }
  }, [existing]);

  const f = Number(first);
  const r = Number(resolution);
  const invalid =
    !Number.isInteger(f) || !Number.isInteger(r) || f <= 0 || r <= 0
      ? 'Targets must be whole, positive minutes.'
      : r < f
        ? 'Resolution target must be at least the first-response target.'
        : null;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (invalid) return;
    try {
      const saved = await upsert.mutateAsync({ priority, firstResponseTargetMinutes: f, resolutionTargetMinutes: r });
      toast.success(`${saved.priority} SLA saved`, `First response ${minutes(saved.firstResponseTargetMinutes)} · resolution ${minutes(saved.resolutionTargetMinutes)}`);
      onSaved(saved.priority);
    } catch {
      /* inline */
    }
  };

  return (
    <Card>
      <CardHeader title={existing ? 'Edit policy' : 'Create policy'} description="PUT upserts the policy for one priority." />
      <CardBody>
        <form onSubmit={submit} className="space-y-4" aria-label="SLA policy editor">
          {upsert.error && <ErrorPanel error={upsert.error} />}
          <Field label="Priority">
            {(id) => (
              <Select id={id} value={priority} onChange={(e) => onPriorityChange(e.target.value as TicketPriority)}>
                {TICKET_PRIORITIES.map((p) => (
                  <option key={p}>{p}</option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="First response target (minutes)" hint={Number.isFinite(f) && f > 0 ? `= ${minutes(f)}` : undefined}>
            {(id) => <Input id={id} type="number" min={1} step={1} value={first} onChange={(e) => setFirst(e.target.value)} />}
          </Field>
          <Field label="Resolution target (minutes)" hint={Number.isFinite(r) && r > 0 ? `= ${minutes(r)}` : undefined} error={invalid}>
            {(id) => <Input id={id} type="number" min={1} step={1} value={resolution} onChange={(e) => setResolution(e.target.value)} />}
          </Field>
          <Button type="submit" className="w-full" icon={<Save className="h-4 w-4" />} loading={upsert.isPending} disabled={Boolean(invalid)}>
            Save {priority} policy
          </Button>
        </form>
      </CardBody>
    </Card>
  );
}
