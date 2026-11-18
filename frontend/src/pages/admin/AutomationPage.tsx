import { Plus, Workflow, Zap } from 'lucide-react';
import { useState } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import {
  Badge,
  Button,
  Card,
  CardHeader,
  EmptyState,
  ErrorPanel,
  ExpandButton,
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
} from '../../components/ui';
import { useAutomationRules, useToggleAutomationRule } from '../../lib/admin';
import { formatRelative } from '../../lib/format';
import type { AutomationRule } from '../../lib/types';
import { toast } from '../../stores/toast';
import { CreateRuleDialog } from './CreateRuleDialog';

/** `GET` / `POST` / `PATCH .../toggle` on `/api/v1/tenant-admin/automation-rules`. */
export function AutomationPage() {
  const { data, isLoading, error, refetch } = useAutomationRules();
  const toggle = useToggleAutomationRule();
  const [open, setOpen] = useState(false);
  const rows = useExpanded();
  const enabled = (data ?? []).filter((r) => r.isEnabled).length;

  return (
    <>
      <PageHeader
        title="Automation Rules"
        description="If-this-then-that rules evaluated by the backend on ticket events."
        actions={
          <Button icon={<Plus className="h-4 w-4" />} onClick={() => setOpen(true)}>
            New rule
          </Button>
        }
      />
      {error ? (
        <ErrorPanel error={error} onRetry={() => void refetch()} />
      ) : (
        <Card>
          <CardHeader
            title="Rules"
            icon={<Workflow className="h-4 w-4" />}
            description={data ? `${data.length} rule${data.length === 1 ? '' : 's'} · ${enabled} enabled` : undefined}
          />
          {isLoading ? (
            <div className="p-5">
              <Skeleton className="h-40 w-full" />
            </div>
          ) : !data?.length ? (
            <EmptyState
              icon={<Zap className="h-5 w-5" />}
              title="No automation rules yet"
              description="Start from a preset template — it takes two clicks."
              action={<Button onClick={() => setOpen(true)}>Create first rule</Button>}
            />
          ) : (
            <Table>
              <THead>
                <tr>
                  <TH className="w-8" />
                  <TH>Rule</TH>
                  <TH>Trigger</TH>
                  <TH>Action</TH>
                  <TH>Created</TH>
                  <TH className="text-right">Enabled</TH>
                </tr>
              </THead>
              <TBody>
                {data.map((rule) => (
                  <RuleRow
                    key={rule.id}
                    open={rows.isOpen(rule.id)}
                    onExpand={() => rows.toggle(rule.id)}
                    rule={rule}
                    onToggle={(isEnabled) =>
                      toggle.mutate(
                        { id: rule.id, isEnabled },
                        { onSuccess: (r) => toast.success(`Rule ${r.isEnabled ? 'enabled' : 'disabled'}`, r.name) },
                      )
                    }
                  />
                ))}
              </TBody>
            </Table>
          )}
        </Card>
      )}
      <CreateRuleDialog open={open} onClose={() => setOpen(false)} />
    </>
  );
}

function RuleRow({
  rule,
  open,
  onExpand,
  onToggle,
}: {
  rule: AutomationRule;
  open: boolean;
  onExpand: () => void;
  onToggle: (next: boolean) => void;
}) {
  return (
    <>
      <TR data-testid={`rule-row-${rule.id}`}>
        <TD>
          <ExpandButton open={open} onClick={onExpand} label={`Show JSON for ${rule.name}`} />
        </TD>
        <TD className="font-medium">{rule.name}</TD>
        <TD>
          <Badge tone="sky" className="font-mono">
            {rule.triggerEvent}
          </Badge>
        </TD>
        <TD>
          <Badge tone="violet" className="font-mono">
            {rule.actionType}
          </Badge>
        </TD>
        <TD className="text-zinc-500">{formatRelative(rule.createdAtUtc)}</TD>
        <TD className="text-right">
          <span className="inline-flex items-center gap-2">
            <span className="text-xs text-zinc-500">{rule.isEnabled ? 'On' : 'Off'}</span>
            <Switch checked={rule.isEnabled} onChange={onToggle} label={`Enable ${rule.name}`} />
          </span>
        </TD>
      </TR>
      {open && (
        <tr className="bg-zinc-50/60 dark:bg-zinc-900/60">
          <td colSpan={6} className="px-4 py-3">
            <div className="grid gap-3 md:grid-cols-2">
              <div>
                <p className="mb-1 text-xs font-medium text-zinc-500">conditionJson</p>
                <JsonBlock json={rule.conditionJson} />
              </div>
              <div>
                <p className="mb-1 text-xs font-medium text-zinc-500">actionPayloadJson</p>
                <JsonBlock json={rule.actionPayloadJson} />
              </div>
            </div>
          </td>
        </tr>
      )}
    </>
  );
}
