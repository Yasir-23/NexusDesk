import { Construction } from 'lucide-react';
import { useLocation } from 'react-router-dom';
import { PageHeader } from '../components/layout/AppShell';
import { Badge, Card } from '../components/ui';
import { NAV_SECTIONS } from '../lib/navigation';

/** Endpoints each upcoming page will consume (from the API contract). */
const PLANNED_ENDPOINTS: Record<string, string[]> = {
  '/tickets': ['GET /api/v1/tickets', 'PATCH /api/v1/tickets/{id}/assign', 'POST /api/v1/ai/tickets/{id}/suggest-reply', 'SignalR /hubs/tickets'],
  '/chat': ['GET /api/v1/chat/sessions', 'POST /api/v1/chat/sessions/{id}/claim', 'SignalR /hubs/chat'],
  '/kb': ['GET /api/v1/kb/categories', 'POST /api/v1/kb/articles', 'GET /api/v1/kb/search'],
  '/analytics': ['GET /api/v1/analytics/overview', 'GET /api/v1/analytics/tickets/trends', 'GET /api/v1/analytics/export/tickets.csv'],
  '/admin/sla': ['GET /api/v1/tenant-admin/sla-policies', 'PUT /api/v1/tenant-admin/sla-policies'],
  '/admin/automation': ['POST /api/v1/tenant-admin/automation-rules', 'PATCH /api/v1/tenant-admin/automation-rules/{id}/toggle'],
  '/admin/webhooks': ['POST /api/v1/tenant-admin/webhooks', 'GET /api/v1/tenant-admin/webhooks/outbox', 'GET /api/v1/tenant-admin/webhooks/dead-letters'],
  '/admin/billing': ['GET /api/v1/billing/plans', 'GET /api/v1/billing/subscription', 'POST /api/v1/billing/subscription/change-plan'],
  '/admin/audit': ['GET /api/v1/audit-logs'],
  '/portal': ['GET /api/v1/portal/tickets', 'POST /api/v1/portal/tickets', 'POST /api/v1/portal/tickets/{id}/replies'],
  '/portal/chat': ['POST /api/v1/chat/sessions', 'SignalR /hubs/chat'],
  '/portal/kb': ['GET /api/v1/kb/search'],
};

export function PlaceholderPage() {
  const { pathname } = useLocation();
  const item = NAV_SECTIONS.flatMap((s) => s.items).find((i) => i.to === pathname);
  const endpoints = PLANNED_ENDPOINTS[pathname] ?? [];
  const milestone = item?.milestone ?? 3;

  return (
    <>
      <PageHeader title={item?.label ?? 'Coming soon'} actions={<Badge tone="indigo">Milestone {milestone}</Badge>} />
      <Card className="flex flex-col items-center px-6 py-14 text-center">
        <div className="rounded-full bg-indigo-50 p-3 text-indigo-500 dark:bg-indigo-500/10">
          <Construction className="h-6 w-6" />
        </div>
        <p className="mt-4 text-sm font-medium">This workspace ships in Milestone {milestone}.</p>
        <p className="mt-1 max-w-md text-sm text-zinc-500">
          The route, navigation and role guard are already wired. It will be built on these backend endpoints:
        </p>
        {endpoints.length > 0 && (
          <ul className="mt-5 space-y-1.5 text-left">
            {endpoints.map((e) => (
              <li key={e} className="rounded-md bg-zinc-100 px-3 py-1.5 font-mono text-xs text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300">
                {e}
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  );
}
