import {
  Activity,
  BarChart3,
  BookOpen,
  Building2,
  CreditCard,
  Gauge,
  LayoutDashboard,
  LifeBuoy,
  MessagesSquare,
  ScrollText,
  ServerCog,
  Ticket,
  Timer,
  UserCircle,
  Webhook,
  Workflow,
  type LucideIcon,
} from 'lucide-react';
import type { Role } from './types';

export function homePathFor(role: Role): string {
  switch (role) {
    case 'SuperAdmin':
      return '/platform/tenants';
    case 'Customer':
      return '/portal';
    default:
      return '/dashboard';
  }
}

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  roles: Role[];
  /** Milestone that implements the page; omitted = live now. */
  milestone?: 2 | 3;
}

export interface NavSection {
  title: string;
  items: NavItem[];
}

const STAFF: Role[] = ['TenantAdmin', 'Agent'];
const ADMIN: Role[] = ['TenantAdmin'];

export const NAV_SECTIONS: NavSection[] = [
  {
    title: 'Platform',
    items: [
      { to: '/platform/tenants', label: 'Tenant Directory', icon: Building2, roles: ['SuperAdmin'] },
      { to: '/platform/system', label: 'System Health', icon: ServerCog, roles: ['SuperAdmin'] },
    ],
  },
  {
    title: 'Workspace',
    items: [
      { to: '/dashboard', label: 'Overview', icon: LayoutDashboard, roles: STAFF },
      { to: '/tickets', label: 'Tickets', icon: Ticket, roles: STAFF, milestone: 2 },
      { to: '/chat', label: 'Live Chat', icon: MessagesSquare, roles: STAFF, milestone: 2 },
      { to: '/kb', label: 'Knowledge Base', icon: BookOpen, roles: STAFF, milestone: 2 },
      { to: '/analytics', label: 'Analytics', icon: BarChart3, roles: STAFF, milestone: 3 },
    ],
  },
  {
    title: 'Administration',
    items: [
      { to: '/admin/sla', label: 'SLA Policies', icon: Timer, roles: ADMIN, milestone: 3 },
      { to: '/admin/automation', label: 'Automation', icon: Workflow, roles: ADMIN, milestone: 3 },
      { to: '/admin/webhooks', label: 'Webhooks & DLQ', icon: Webhook, roles: ADMIN, milestone: 3 },
      { to: '/admin/billing', label: 'Billing & Usage', icon: CreditCard, roles: ADMIN, milestone: 3 },
      { to: '/admin/audit', label: 'Audit Log', icon: ScrollText, roles: ADMIN, milestone: 3 },
    ],
  },
  {
    title: 'Support',
    items: [
      { to: '/portal', label: 'My Requests', icon: LifeBuoy, roles: ['Customer'] },
      { to: '/portal/chat', label: 'Live Chat', icon: MessagesSquare, roles: ['Customer'], milestone: 2 },
      { to: '/portal/kb', label: 'Help Center', icon: BookOpen, roles: ['Customer'], milestone: 2 },
    ],
  },
  {
    title: 'Account',
    items: [
      { to: '/account', label: 'My Profile', icon: UserCircle, roles: ['SuperAdmin', 'TenantAdmin', 'Agent', 'Customer'] },
    ],
  },
];

export function navFor(role: Role): NavSection[] {
  return NAV_SECTIONS.map((s) => ({ ...s, items: s.items.filter((i) => i.roles.includes(role)) })).filter(
    (s) => s.items.length > 0,
  );
}

export const ROLE_META: Record<Role, { label: string; icon: LucideIcon }> = {
  SuperAdmin: { label: 'Super Admin', icon: Gauge },
  TenantAdmin: { label: 'Tenant Admin', icon: Building2 },
  Agent: { label: 'Agent', icon: Activity },
  Customer: { label: 'Customer', icon: UserCircle },
};
