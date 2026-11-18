import { Sparkles } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Button, Dialog, ErrorPanel, Field, Input, Select, Switch, Textarea } from '../../components/ui';
import { useCreateAutomationRule } from '../../lib/admin';
import { cn } from '../../lib/cn';
import { useTenantUsers } from '../../lib/tickets';
import {
  AUTOMATION_ACTIONS,
  AUTOMATION_TRIGGERS,
  type AutomationActionType,
  type AutomationTriggerEvent,
  type CreateAutomationRuleRequest,
} from '../../lib/types';
import { toast } from '../../stores/toast';

interface Preset {
  id: string;
  label: string;
  description: string;
  build: (ctx: { agentId: string | null }) => CreateAutomationRuleRequest;
}

const json = (v: unknown) => JSON.stringify(v);

/**
 * One-click templates. Condition keys are the ones the backend evaluator matches
 * (`priority`, `oldPriority`, `breachType`, `ticketNumber`, `triggerEvent`) and payload keys
 * are the ones each action reads (`priority`, `agentId`, `body`).
 */
export const RULE_PRESETS: Preset[] = [
  {
    id: 'escalate-high',
    label: 'Auto-escalate High tickets to Urgent',
    description: 'TicketCreated · priority = High → EscalatePriority to Urgent',
    build: () => ({
      name: 'Auto-escalate High tickets to Urgent',
      triggerEvent: 'TicketCreated',
      conditionJson: json({ priority: 'High' }),
      actionType: 'EscalatePriority',
      actionPayloadJson: json({ priority: 'Urgent' }),
      isEnabled: true,
    }),
  },
  {
    id: 'escalate-breach',
    label: 'Escalate on SLA breach',
    description: 'TicketSlaBreached → EscalatePriority one level',
    build: () => ({
      name: 'Escalate on SLA breach',
      triggerEvent: 'TicketSlaBreached',
      conditionJson: json({}),
      actionType: 'EscalatePriority',
      actionPayloadJson: json({}),
      isEnabled: true,
    }),
  },
  {
    id: 'route-urgent',
    label: 'Route Urgent tickets to an on-call agent',
    description: 'TicketCreated · priority = Urgent → AssignAgent',
    build: ({ agentId }) => ({
      name: 'Route Urgent tickets to on-call agent',
      triggerEvent: 'TicketCreated',
      conditionJson: json({ priority: 'Urgent' }),
      actionType: 'AssignAgent',
      actionPayloadJson: json({ agentId: agentId ?? '00000000-0000-0000-0000-000000000000' }),
      isEnabled: true,
    }),
  },
  {
    id: 'note-downgrade',
    label: 'Flag Urgent downgrades',
    description: 'TicketPriorityChanged · oldPriority = Urgent → AddInternalNote',
    build: () => ({
      name: 'Flag Urgent downgrades for review',
      triggerEvent: 'TicketPriorityChanged',
      conditionJson: json({ oldPriority: 'Urgent' }),
      actionType: 'AddInternalNote',
      actionPayloadJson: json({ body: 'Priority was lowered from Urgent — team lead please review.' }),
      isEnabled: true,
    }),
  },
];

function jsonObjectError(text: string): string | null {
  if (!text.trim()) return null;
  try {
    const v: unknown = JSON.parse(text);
    return v && typeof v === 'object' && !Array.isArray(v) ? null : 'Must be a JSON object, e.g. {"priority":"High"}';
  } catch {
    return 'Invalid JSON';
  }
}

const EMPTY: CreateAutomationRuleRequest = {
  name: '',
  triggerEvent: 'TicketCreated',
  conditionJson: '{}',
  actionType: 'EscalatePriority',
  actionPayloadJson: '{}',
  isEnabled: true,
};

export function CreateRuleDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const create = useCreateAutomationRule();
  const users = useTenantUsers(open);
  const agentId = (users.data ?? []).find((u) => u.role === 'Agent' && u.isActive && !u.isAnonymized)?.id ?? null;
  const [form, setForm] = useState<CreateAutomationRuleRequest>(EMPTY);
  const [presetId, setPresetId] = useState<string | null>(null);

  const set = <K extends keyof CreateAutomationRuleRequest>(k: K, v: CreateAutomationRuleRequest[K]) => {
    setForm((f) => ({ ...f, [k]: v }));
    setPresetId(null);
  };

  const close = () => {
    setForm(EMPTY);
    setPresetId(null);
    create.reset();
    onClose();
  };

  const conditionError = jsonObjectError(form.conditionJson);
  const payloadError = jsonObjectError(form.actionPayloadJson);
  const canSubmit = form.name.trim().length > 0 && !conditionError && !payloadError;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    try {
      const rule = await create.mutateAsync({
        ...form,
        name: form.name.trim(),
        conditionJson: form.conditionJson.trim() || '{}',
        actionPayloadJson: form.actionPayloadJson.trim() || '{}',
      });
      toast.success('Automation rule created', rule.name);
      close();
    } catch {
      /* inline */
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      size="lg"
      title="New automation rule"
      description="Pick a template or define the trigger, condition and action yourself."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form="create-rule-form" loading={create.isPending} disabled={!canSubmit}>
            Create rule
          </Button>
        </>
      }
    >
      <form id="create-rule-form" onSubmit={submit} className="space-y-4">
        {create.error && <ErrorPanel error={create.error} />}
        <div role="group" aria-label="Rule templates" className="grid gap-2 sm:grid-cols-2">
          {RULE_PRESETS.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => {
                setForm(p.build({ agentId }));
                setPresetId(p.id);
              }}
              aria-pressed={presetId === p.id}
              className={cn(
                'rounded-lg border p-3 text-left transition-colors',
                presetId === p.id
                  ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-500/10'
                  : 'border-zinc-200 hover:border-indigo-300 hover:bg-zinc-50 dark:border-zinc-700 dark:hover:bg-zinc-800',
              )}
            >
              <span className="flex items-center gap-1.5 text-sm font-medium">
                <Sparkles className="h-3.5 w-3.5 text-indigo-500" aria-hidden /> {p.label}
              </span>
              <span className="mt-0.5 block font-mono text-[11px] text-zinc-500">{p.description}</span>
            </button>
          ))}
        </div>
        <Field label="Rule name">
          {(id) => <Input id={id} value={form.name} onChange={(e) => set('name', e.target.value)} maxLength={150} required />}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Trigger event">
            {(id) => (
              <Select id={id} value={form.triggerEvent} onChange={(e) => set('triggerEvent', e.target.value as AutomationTriggerEvent)}>
                {AUTOMATION_TRIGGERS.map((t) => (
                  <option key={t}>{t}</option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Action type">
            {(id) => (
              <Select id={id} value={form.actionType} onChange={(e) => set('actionType', e.target.value as AutomationActionType)}>
                {AUTOMATION_ACTIONS.map((a) => (
                  <option key={a}>{a}</option>
                ))}
              </Select>
            )}
          </Field>
        </div>
        <Field label="Condition JSON" hint="Matched keys: priority, oldPriority, breachType, ticketNumber" error={conditionError}>
          {(id) => (
            <Textarea id={id} rows={2} className="font-mono text-xs" value={form.conditionJson} onChange={(e) => set('conditionJson', e.target.value)} />
          )}
        </Field>
        <Field label="Action payload JSON" hint="EscalatePriority: priority · AssignAgent: agentId · AddInternalNote: body" error={payloadError}>
          {(id) => (
            <Textarea
              id={id}
              rows={2}
              className="font-mono text-xs"
              value={form.actionPayloadJson}
              onChange={(e) => set('actionPayloadJson', e.target.value)}
            />
          )}
        </Field>
        <label className="flex items-center gap-3 text-sm">
          <Switch checked={form.isEnabled} onChange={(v) => set('isEnabled', v)} label="Enable rule on creation" />
          Enabled on creation
        </label>
      </form>
    </Dialog>
  );
}
