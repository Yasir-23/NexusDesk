import { Check, CreditCard, Gauge, Minus, UserPlus, Users } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { PageHeader } from '../../components/layout/AppShell';
import { PlanBadge, RoleBadge } from '../../components/domain/Badges';
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ErrorPanel,
  Skeleton,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  UsageMeter,
} from '../../components/ui';
import { USAGE_LABELS, formatPrice, useBillingPlans, useChangePlan, useSubscription } from '../../lib/billing';
import { cn } from '../../lib/cn';
import { formatDate, formatQuota, formatRelative } from '../../lib/format';
import { useTenantUsers } from '../../lib/tickets';
import type { PlanCode, SubscriptionPlan } from '../../lib/types';
import { toast } from '../../stores/toast';
import { AddAgentDialog } from './AddAgentDialog';

export function BillingPage() {
  const sub = useSubscription();
  const [agentOpen, setAgentOpen] = useState(false);

  return (
    <>
      <PageHeader
        title="Billing & Usage"
        description="Your subscription, live quota meters, plan catalog and agent seats."
        actions={
          <Button icon={<UserPlus className="h-4 w-4" />} onClick={() => setAgentOpen(true)}>
            Add agent
          </Button>
        }
      />
      <div className="space-y-4">
        {sub.error ? (
          <ErrorPanel error={sub.error} onRetry={() => void sub.refetch()} />
        ) : !sub.data ? (
          <Skeleton className="h-48 w-full" />
        ) : (
          <div className="grid gap-4 lg:grid-cols-[20rem_1fr]">
            <Card data-testid="current-plan">
              <CardHeader title="Current plan" icon={<CreditCard className="h-4 w-4" />} actions={<PlanBadge plan={sub.data.plan.code} />} />
              <CardBody>
                <p className="text-lg font-semibold">{sub.data.plan.name}</p>
                <p className="text-2xl font-semibold tabular-nums">
                  {formatPrice(sub.data.plan.monthlyPriceCents)}
                  <span className="text-sm font-normal text-zinc-500"> / month</span>
                </p>
                <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
                  <dt className="text-zinc-500">Status</dt>
                  <dd>
                    <Badge tone={sub.data.subscription.status === 'Active' ? 'green' : 'amber'} dot>
                      {sub.data.subscription.status}
                    </Badge>
                  </dd>
                  <dt className="text-zinc-500">Billing cycle</dt>
                  <dd data-testid="billing-cycle">{sub.data.subscription.billingCycle}</dd>
                  <dt className="text-zinc-500">Period</dt>
                  <dd>
                    {formatDate(sub.data.subscription.currentPeriodStartUtc)} – {formatDate(sub.data.subscription.currentPeriodEndUtc)}
                  </dd>
                  {sub.data.subscription.cancelAtPeriodEnd && (
                    <>
                      <dt className="text-zinc-500">Renewal</dt>
                      <dd>
                        <Badge tone="amber">Cancels at period end</Badge>
                      </dd>
                    </>
                  )}
                </dl>
              </CardBody>
            </Card>
            <Card>
              <CardHeader title="Usage this period" icon={<Gauge className="h-4 w-4" />} description={`Billing period ${sub.data.periodKey}`} />
              <CardBody className="space-y-5">
                {sub.data.usage.map((u) => (
                  <UsageMeter
                    key={u.metricName}
                    testId={`usage-${u.metricName}`}
                    label={USAGE_LABELS[u.metricName] ?? u.metricName}
                    used={u.used}
                    limit={u.limit}
                    percentage={u.usagePercentage}
                    format={u.metricName === 'ai_tokens_used' ? formatQuota : undefined}
                  />
                ))}
              </CardBody>
            </Card>
          </div>
        )}
        <PlanCatalog currentPlan={sub.data?.plan.code} billingCycle={sub.data?.subscription.billingCycle ?? 'Monthly'} />
        <TeamCard onAdd={() => setAgentOpen(true)} maxAgents={sub.data?.plan.maxAgents} />
      </div>
      <AddAgentDialog open={agentOpen} onClose={() => setAgentOpen(false)} />
    </>
  );
}

function PlanCatalog({ currentPlan, billingCycle }: { currentPlan: PlanCode | undefined; billingCycle: string }) {
  const plans = useBillingPlans();
  const change = useChangePlan();
  // Ordered by price, so extra catalog tiers (e.g. `Starter`) slot in naturally.
  const sorted = [...(plans.data ?? [])].filter((p) => p.isActive).sort((a, b) => a.monthlyPriceCents - b.monthlyPriceCents);
  const currentPrice = sorted.find((p) => p.code === currentPlan)?.monthlyPriceCents ?? -1;

  const switchTo = (plan: SubscriptionPlan) =>
    change.mutate(
      { planCode: plan.code, billingCycle },
      { onSuccess: (d) => toast.success(`Switched to ${d.plan.name}`, `Plan ${d.plan.code} · ${d.subscription.billingCycle}`) },
    );

  return (
    <Card>
      <CardHeader title="Plans" description="Changes apply immediately; usage limits update with the plan." />
      <CardBody className="space-y-4">
        {change.error && <ErrorPanel error={change.error} />}
        {plans.error ? (
          <ErrorPanel error={plans.error} onRetry={() => void plans.refetch()} />
        ) : !plans.data ? (
          <Skeleton className="h-56 w-full" />
        ) : (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {sorted.map((plan) => {
              const current = plan.code === currentPlan;
              const upgrade = plan.monthlyPriceCents > currentPrice;
              return (
                <div
                  key={plan.id}
                  data-testid={`plan-card-${plan.code}`}
                  className={cn(
                    'flex flex-col rounded-xl border p-5',
                    current ? 'border-indigo-500 ring-2 ring-indigo-500/20' : 'border-zinc-200 dark:border-zinc-800',
                  )}
                >
                  <div className="flex items-center justify-between gap-2">
                    <PlanBadge plan={plan.code} />
                    {current && <Badge tone="indigo">Current plan</Badge>}
                  </div>
                  <p className="mt-3 font-semibold">{plan.name}</p>
                  <p className="text-2xl font-semibold tabular-nums">
                    {formatPrice(plan.monthlyPriceCents)}
                    <span className="text-sm font-normal text-zinc-500"> / mo</span>
                  </p>
                  <ul className="mt-4 flex-1 space-y-1.5 text-sm">
                    <Feature on>{formatQuota(plan.maxAgents)} agents</Feature>
                    <Feature on>{formatQuota(plan.maxMonthlyTickets)} tickets / month</Feature>
                    <Feature on>{formatQuota(plan.maxMonthlyAiTokens)} AI tokens / month</Feature>
                    <Feature on={plan.slaEnabled}>SLA policies</Feature>
                    <Feature on={plan.webhooksEnabled}>Webhooks</Feature>
                    <Feature on={plan.customBrandingEnabled}>Custom branding</Feature>
                  </ul>
                  <Button
                    className="mt-5 w-full"
                    variant={current ? 'secondary' : upgrade ? 'primary' : 'secondary'}
                    disabled={current || change.isPending}
                    loading={change.isPending && change.variables?.planCode === plan.code}
                    onClick={() => switchTo(plan)}
                  >
                    {current ? 'Current plan' : upgrade ? `Upgrade to ${plan.code}` : `Switch to ${plan.code}`}
                  </Button>
                </div>
              );
            })}
          </div>
        )}
      </CardBody>
    </Card>
  );
}

function Feature({ on, children }: { on: boolean; children: ReactNode }) {
  return (
    <li className={cn('flex items-center gap-2', !on && 'text-zinc-400 line-through')}>
      {on ? <Check className="h-4 w-4 text-emerald-500" aria-hidden /> : <Minus className="h-4 w-4" aria-hidden />}
      <span>{children}</span>
      <span className="sr-only">{on ? '(included)' : '(not included)'}</span>
    </li>
  );
}

function TeamCard({ onAdd, maxAgents }: { onAdd: () => void; maxAgents: number | undefined }) {
  const users = useTenantUsers();
  const staff = (users.data ?? []).filter((u) => u.role === 'Agent' || u.role === 'TenantAdmin');
  const agents = staff.filter((u) => u.role === 'Agent' && u.isActive && !u.isAnonymized).length;

  return (
    <Card>
      <CardHeader
        title="Team members"
        icon={<Users className="h-4 w-4" />}
        description={maxAgents != null ? `${agents} active agent${agents === 1 ? '' : 's'} · plan allows ${maxAgents}` : undefined}
        actions={
          <Button size="sm" variant="secondary" icon={<UserPlus className="h-3.5 w-3.5" />} onClick={onAdd}>
            Add agent
          </Button>
        }
      />
      {users.error ? (
        <CardBody>
          <ErrorPanel error={users.error} onRetry={() => void users.refetch()} />
        </CardBody>
      ) : users.isLoading ? (
        <CardBody>
          <Skeleton className="h-24 w-full" />
        </CardBody>
      ) : (
        <Table>
          <THead>
            <tr>
              <TH>Name</TH>
              <TH>Email</TH>
              <TH>Role</TH>
              <TH>Status</TH>
              <TH>Last login</TH>
            </tr>
          </THead>
          <TBody>
            {staff.map((u) => (
              <TR key={u.id} data-testid={`member-row-${u.email}`}>
                <TD className="font-medium">{u.fullName}</TD>
                <TD className="text-zinc-600 dark:text-zinc-400">{u.email}</TD>
                <TD>
                  <RoleBadge role={u.role} />
                </TD>
                <TD>
                  <Badge tone={u.isActive ? 'green' : 'neutral'} dot>
                    {u.isAnonymized ? 'Erased' : u.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </TD>
                <TD className="text-zinc-500">{formatRelative(u.lastLoginAtUtc)}</TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  );
}
