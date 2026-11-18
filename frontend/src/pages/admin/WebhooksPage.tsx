import { Inbox, KeyRound, RefreshCw, Skull, Webhook } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorPanel,
  ExpandButton,
  Field,
  Input,
  JsonBlock,
  Skeleton,
  Switch,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  useExpanded,
  type BadgeTone,
} from '../../components/ui';
import { useCreateWebhook, useDeadLetters, useOutbox, useWebhooks } from '../../lib/admin';
import { cn } from '../../lib/cn';
import { formatDateTime, formatRelative } from '../../lib/format';
import { WEBHOOK_EVENT_TYPES, type OutboxStatus, type WebhookEventType } from '../../lib/types';
import { toast } from '../../stores/toast';

/** Groups the backend's `Processed` / `DeadLettered` with the contract's `Dispatched` / `Failed`. */
export type OutboxBucket = 'Pending' | 'Dispatched' | 'Failed';

export function outboxBucket(status: OutboxStatus | string): OutboxBucket {
  if (status === 'Processed' || status === 'Dispatched') return 'Dispatched';
  if (status === 'DeadLettered' || status === 'Failed') return 'Failed';
  return 'Pending';
}

const BUCKET_TONES: Record<OutboxBucket, BadgeTone> = { Pending: 'amber', Dispatched: 'green', Failed: 'red' };

export function WebhooksPage() {
  return (
    <>
      <PageHeader
        title="Webhooks & Dead-Letter Queue"
        description="Outbound event subscriptions, the transactional outbox feeding them, and deliveries that exhausted their retries."
      />
      <div className="space-y-4">
        <div className="grid gap-4 lg:grid-cols-[22rem_1fr]">
          <RegisterWebhookCard />
          <SubscriptionsCard />
        </div>
        <OutboxCard />
        <DeadLettersCard />
      </div>
    </>
  );
}

function RegisterWebhookCard() {
  const create = useCreateWebhook();
  const [targetUrl, setTargetUrl] = useState('https://webhook.site/test-receiver');
  const [secret, setSecret] = useState('');
  const [events, setEvents] = useState<Set<WebhookEventType>>(() => new Set(WEBHOOK_EVENT_TYPES));
  const [isActive, setIsActive] = useState(true);

  const secretError = secret && secret.trim().length < 16 ? 'Secret must be at least 16 characters.' : null;
  const canSubmit = targetUrl.trim() && secret.trim().length >= 16 && events.size > 0;

  const generateSecret = () => {
    const bytes = crypto.getRandomValues(new Uint8Array(18));
    setSecret(`whsec_${Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')}`);
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    try {
      const sub = await create.mutateAsync({
        targetUrl: targetUrl.trim(),
        secret: secret.trim(),
        eventTypes: WEBHOOK_EVENT_TYPES.filter((t) => events.has(t)),
        isActive,
      });
      toast.success('Webhook registered', sub.targetUrl);
      setSecret('');
    } catch {
      /* inline */
    }
  };

  return (
    <Card>
      <CardHeader title="Register webhook" icon={<Webhook className="h-4 w-4" />} description="Payloads are HMAC-signed with the secret." />
      <CardBody>
        <form onSubmit={submit} className="space-y-4" aria-label="Register webhook">
          {create.error && <ErrorPanel error={create.error} />}
          <Field label="Target URL" hint="HTTPS required (HTTP allowed for localhost).">
            {(id) => <Input id={id} type="url" value={targetUrl} onChange={(e) => setTargetUrl(e.target.value)} required />}
          </Field>
          <Field label="Signing secret" error={secretError}>
            {(id) => (
              <div className="flex gap-2">
                <Input id={id} value={secret} onChange={(e) => setSecret(e.target.value)} placeholder="whsec_…" className="font-mono text-xs" />
                <Button variant="secondary" size="icon" onClick={generateSecret} aria-label="Generate secret" title="Generate secret">
                  <KeyRound className="h-4 w-4" />
                </Button>
              </div>
            )}
          </Field>
          <fieldset>
            <legend className="mb-1.5 text-xs font-medium text-zinc-700 dark:text-zinc-300">Event types</legend>
            <div className="space-y-1.5">
              {WEBHOOK_EVENT_TYPES.map((t) => (
                <label key={t} className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-zinc-300 accent-indigo-600"
                    checked={events.has(t)}
                    onChange={(e) =>
                      setEvents((prev) => {
                        const next = new Set(prev);
                        if (e.target.checked) next.add(t);
                        else next.delete(t);
                        return next;
                      })
                    }
                  />
                  <span className="font-mono text-xs">{t}</span>
                </label>
              ))}
            </div>
          </fieldset>
          <label className="flex items-center gap-3 text-sm">
            <Switch checked={isActive} onChange={setIsActive} label="Webhook active" /> Active
          </label>
          <Button type="submit" className="w-full" loading={create.isPending} disabled={!canSubmit}>
            Register webhook
          </Button>
        </form>
      </CardBody>
    </Card>
  );
}

function SubscriptionsCard() {
  const { data, isLoading, error, refetch } = useWebhooks();
  return (
    <Card>
      <CardHeader title="Subscriptions" description={data ? `${data.length} registered` : undefined} />
      {error ? (
        <CardBody>
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        </CardBody>
      ) : isLoading ? (
        <CardBody>
          <Skeleton className="h-32 w-full" />
        </CardBody>
      ) : !data?.length ? (
        <EmptyState icon={<Webhook className="h-5 w-5" />} title="No webhooks registered" description="Register an endpoint to receive ticket events." />
      ) : (
        <Table>
          <THead>
            <tr>
              <TH>Target</TH>
              <TH>Events</TH>
              <TH>Status</TH>
              <TH>Created</TH>
            </tr>
          </THead>
          <TBody>
            {data.map((w) => (
              <TR key={w.id} data-testid={`webhook-row-${w.id}`}>
                <TD className="max-w-xs truncate font-mono text-xs" title={w.targetUrl}>
                  {w.targetUrl}
                </TD>
                <TD>
                  <div className="flex flex-wrap gap-1">
                    {w.eventTypes.map((t) => (
                      <Badge key={t} tone="sky" className="font-mono">
                        {t}
                      </Badge>
                    ))}
                  </div>
                </TD>
                <TD>
                  <Badge tone={w.isActive ? 'green' : 'neutral'} dot>
                    {w.isActive ? 'Active' : 'Paused'}
                  </Badge>
                </TD>
                <TD className="text-zinc-500">{formatRelative(w.createdAtUtc)}</TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  );
}

function OutboxCard() {
  const { data, isLoading, error, refetch, isFetching } = useOutbox();
  const [filter, setFilter] = useState<OutboxBucket | 'All'>('All');
  const rows = useExpanded();
  const counts = { Pending: 0, Dispatched: 0, Failed: 0 } as Record<OutboxBucket, number>;
  for (const m of data ?? []) counts[outboxBucket(m.status)]++;
  const visible = (data ?? []).filter((m) => filter === 'All' || outboxBucket(m.status) === filter);

  return (
    <Card data-testid="outbox-inspector">
      <CardHeader
        title="Transactional outbox"
        icon={<Inbox className="h-4 w-4" />}
        description="Latest 100 events written atomically with the domain change, then dispatched by the background worker. Auto-refreshes every 10s."
        actions={
          <Button variant="ghost" size="sm" onClick={() => void refetch()} icon={<RefreshCw className={cn('h-3.5 w-3.5', isFetching && 'animate-spin')} />}>
            Refresh
          </Button>
        }
      />
      <div role="group" aria-label="Filter outbox by status" className="flex flex-wrap gap-2 border-b border-zinc-100 px-5 py-3 dark:border-zinc-800">
        {(['All', 'Pending', 'Dispatched', 'Failed'] as const).map((b) => (
          <button
            key={b}
            type="button"
            aria-pressed={filter === b}
            onClick={() => setFilter(b)}
            className={cn(
              'rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset transition-colors',
              filter === b
                ? 'bg-zinc-900 text-white ring-zinc-900 dark:bg-zinc-100 dark:text-zinc-900'
                : 'text-zinc-600 ring-zinc-200 hover:bg-zinc-100 dark:text-zinc-300 dark:ring-zinc-700 dark:hover:bg-zinc-800',
            )}
          >
            {b} <span className="tabular-nums opacity-70">{b === 'All' ? (data?.length ?? 0) : counts[b]}</span>
          </button>
        ))}
      </div>
      {error ? (
        <CardBody>
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        </CardBody>
      ) : isLoading ? (
        <CardBody>
          <Skeleton className="h-32 w-full" />
        </CardBody>
      ) : visible.length === 0 ? (
        <EmptyState icon={<Inbox className="h-5 w-5" />} title={filter === 'All' ? 'Outbox is empty' : `No ${filter.toLowerCase()} messages`} />
      ) : (
        <Table>
          <THead>
            <tr>
              <TH className="w-8" />
              <TH>Event</TH>
              <TH>Status</TH>
              <TH>Retries</TH>
              <TH>Created</TH>
              <TH>Processed / next retry</TH>
              <TH>Last error</TH>
            </tr>
          </THead>
          <TBody>
            {visible.map((m) => {
              const bucket = outboxBucket(m.status);
              return (
                <Expandable key={m.id} open={rows.isOpen(m.id)} colSpan={7} json={m.payloadJson}>
                  <TR data-testid={`outbox-row-${m.id}`}>
                    <TD>
                      <ExpandButton open={rows.isOpen(m.id)} onClick={() => rows.toggle(m.id)} label={`Show payload for ${m.eventType}`} />
                    </TD>
                    <TD className="font-mono text-xs">{m.eventType}</TD>
                    <TD>
                      <Badge tone={BUCKET_TONES[bucket]} dot>
                        {bucket}
                      </Badge>
                      {bucket !== m.status && <span className="ml-1.5 font-mono text-[10px] text-zinc-400">{m.status}</span>}
                    </TD>
                    <TD className="tabular-nums">{m.retryCount}</TD>
                    <TD className="whitespace-nowrap text-zinc-500">{formatDateTime(m.createdAtUtc)}</TD>
                    <TD className="whitespace-nowrap text-zinc-500">
                      {m.processedAtUtc ? formatDateTime(m.processedAtUtc) : bucket === 'Pending' ? `retry ${formatDateTime(m.nextRetryAtUtc)}` : '—'}
                    </TD>
                    <TD className="max-w-xs truncate text-xs text-rose-600 dark:text-rose-400" title={m.lastError ?? undefined}>
                      {m.lastError ?? <span className="text-zinc-400">—</span>}
                    </TD>
                  </TR>
                </Expandable>
              );
            })}
          </TBody>
        </Table>
      )}
    </Card>
  );
}

function DeadLettersCard() {
  const { data, isLoading, error, refetch } = useDeadLetters();
  const rows = useExpanded();
  return (
    <Card data-testid="dlq-inspector">
      <CardHeader
        title="Dead-letter queue"
        icon={<Skull className="h-4 w-4" />}
        description="Deliveries that failed every retry. Nothing is lost: each keeps its payload, attempt count and last receiver response."
        actions={data?.length ? <Badge tone="red">{data.length} dead-lettered</Badge> : <Badge tone="green">Healthy</Badge>}
      />
      {error ? (
        <CardBody>
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        </CardBody>
      ) : isLoading ? (
        <CardBody>
          <Skeleton className="h-32 w-full" />
        </CardBody>
      ) : !data?.length ? (
        <EmptyState icon={<Skull className="h-5 w-5" />} title="Dead-letter queue is empty" description="Every webhook delivery has succeeded or is still retrying." />
      ) : (
        <Table>
          <THead>
            <tr>
              <TH className="w-8" />
              <TH>Event</TH>
              <TH>Target</TH>
              <TH>Attempts</TH>
              <TH>Last HTTP</TH>
              <TH>Last error</TH>
              <TH>Failed at</TH>
            </tr>
          </THead>
          <TBody>
            {data.map((d) => (
              <Expandable key={d.id} open={rows.isOpen(d.id)} colSpan={7} json={d.payloadJson}>
                <TR data-testid={`dlq-row-${d.id}`}>
                  <TD>
                    <ExpandButton open={rows.isOpen(d.id)} onClick={() => rows.toggle(d.id)} label={`Show payload for ${d.eventType}`} />
                  </TD>
                  <TD className="font-mono text-xs">{d.eventType}</TD>
                  <TD className="max-w-[14rem] truncate font-mono text-xs" title={d.targetUrl}>
                    {d.targetUrl}
                  </TD>
                  <TD className="tabular-nums">{d.attemptCount}</TD>
                  <TD>
                    {d.lastHttpStatusCode != null ? (
                      <Badge tone={d.lastHttpStatusCode >= 500 ? 'red' : 'amber'} className="font-mono">
                        HTTP {d.lastHttpStatusCode}
                      </Badge>
                    ) : (
                      <Badge tone="neutral">No response</Badge>
                    )}
                  </TD>
                  <TD className="max-w-xs text-xs text-rose-600 dark:text-rose-400">{d.lastError}</TD>
                  <TD className="whitespace-nowrap text-zinc-500">{formatDateTime(d.failedAtUtc)}</TD>
                </TR>
              </Expandable>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  );
}

function Expandable({ open, colSpan, json, children }: { open: boolean; colSpan: number; json: string; children: ReactNode }) {
  return (
    <>
      {children}
      {open && (
        <tr className="bg-zinc-50/60 dark:bg-zinc-900/60">
          <td colSpan={colSpan} className="px-4 py-3">
            <p className="mb-1 text-xs font-medium text-zinc-500">payloadJson</p>
            <JsonBlock json={json} />
          </td>
        </tr>
      )}
    </>
  );
}
