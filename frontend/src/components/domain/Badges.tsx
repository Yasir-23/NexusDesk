import { Database, DatabaseZap, Globe, Layers } from 'lucide-react';
import { Badge, type BadgeTone } from '../ui';
import { ROLE_META } from '../../lib/navigation';
import type { IsolationStrategy, PlanCode, Role, TenantPlanTier, TenantStatus } from '../../lib/types';

const planTones: Record<TenantPlanTier, BadgeTone> = { Free: 'neutral', Pro: 'indigo', Enterprise: 'violet' };
const toneFor = (plan: PlanCode): BadgeTone => planTones[plan as TenantPlanTier] ?? 'sky';

export function PlanBadge({ plan }: { plan: PlanCode }) {
  return (
    <Badge tone={toneFor(plan)} icon={<Layers className="h-3 w-3" aria-hidden />}>
      {plan}
    </Badge>
  );
}

export function StatusBadge({ status }: { status: TenantStatus }) {
  return (
    <Badge tone={status === 'Active' ? 'green' : 'red'} dot>
      {status}
    </Badge>
  );
}

export function IsolationBadge({ strategy, compact }: { strategy: IsolationStrategy | 'Platform'; compact?: boolean }) {
  if (strategy === 'Platform') {
    return (
      <Badge tone="sky" icon={<Globe className="h-3 w-3" aria-hidden />}>
        Platform
      </Badge>
    );
  }
  const dedicated = strategy === 'DedicatedDatabase';
  const Icon = dedicated ? DatabaseZap : Database;
  return (
    <Badge tone={dedicated ? 'amber' : 'teal'} icon={<Icon className="h-3 w-3" aria-hidden />}>
      {compact ? (dedicated ? 'Dedicated DB' : 'Shared · RLS') : strategy}
    </Badge>
  );
}

export function RoleBadge({ role }: { role: Role }) {
  const tone: Record<Role, BadgeTone> = { SuperAdmin: 'sky', TenantAdmin: 'violet', Agent: 'indigo', Customer: 'neutral' };
  return <Badge tone={tone[role]}>{ROLE_META[role]?.label ?? role}</Badge>;
}
