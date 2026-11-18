import { lazy, type ReactNode } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { AppShell } from './components/layout/AppShell';
import { homePathFor } from './lib/navigation';
import type { Role } from './lib/types';
import { AccountPage } from './pages/AccountPage';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/auth/LoginPage';
import { RegisterPage } from './pages/auth/RegisterPage';
import { selectIsAuthenticated, useAuthStore } from './stores/auth';

// Workspace pages are code-split per route; AppShell renders a Suspense fallback around <Outlet />.
const TenantDirectoryPage = lazy(() => import('./pages/platform/TenantDirectoryPage').then((m) => ({ default: m.TenantDirectoryPage })));
const SystemHealthPage = lazy(() => import('./pages/platform/SystemHealthPage').then((m) => ({ default: m.SystemHealthPage })));
const TicketQueuePage = lazy(() => import('./pages/tickets/TicketQueuePage').then((m) => ({ default: m.TicketQueuePage })));
const TicketDetailPage = lazy(() => import('./pages/tickets/TicketDetailPage').then((m) => ({ default: m.TicketDetailPage })));
const ChatPage = lazy(() => import('./pages/chat/ChatPage').then((m) => ({ default: m.ChatPage })));
const KnowledgeBasePage = lazy(() => import('./pages/kb/KnowledgeBasePage').then((m) => ({ default: m.KnowledgeBasePage })));
const AnalyticsPage = lazy(() => import('./pages/analytics/AnalyticsPage').then((m) => ({ default: m.AnalyticsPage })));
const SlaPoliciesPage = lazy(() => import('./pages/admin/SlaPoliciesPage').then((m) => ({ default: m.SlaPoliciesPage })));
const AutomationPage = lazy(() => import('./pages/admin/AutomationPage').then((m) => ({ default: m.AutomationPage })));
const WebhooksPage = lazy(() => import('./pages/admin/WebhooksPage').then((m) => ({ default: m.WebhooksPage })));
const BillingPage = lazy(() => import('./pages/admin/BillingPage').then((m) => ({ default: m.BillingPage })));
const AuditLogPage = lazy(() => import('./pages/admin/AuditLogPage').then((m) => ({ default: m.AuditLogPage })));
const PortalPage = lazy(() => import('./pages/portal/PortalPage').then((m) => ({ default: m.PortalPage })));
const PortalTicketPage = lazy(() => import('./pages/portal/PortalTicketPage').then((m) => ({ default: m.PortalTicketPage })));

/** Redirects anonymous users to /login and users without an allowed role to their home. */
export function RequireAuth({ roles, children }: { roles?: Role[]; children: ReactNode }) {
  const isAuthenticated = useAuthStore(selectIsAuthenticated);
  const role = useAuthStore((s) => s.user?.role);
  const location = useLocation();

  if (!isAuthenticated || !role) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  if (roles && !roles.includes(role)) {
    return <Navigate to={homePathFor(role)} replace />;
  }
  return <>{children}</>;
}

function PublicOnly({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore(selectIsAuthenticated);
  const role = useAuthStore((s) => s.user?.role);
  if (isAuthenticated && role) return <Navigate to={homePathFor(role)} replace />;
  return <>{children}</>;
}

function HomeRedirect() {
  const role = useAuthStore((s) => s.user?.role);
  return <Navigate to={role ? homePathFor(role) : '/login'} replace />;
}

const STAFF: Role[] = ['TenantAdmin', 'Agent'];
const ADMIN: Role[] = ['TenantAdmin'];
const TENANT_USERS: Role[] = ['TenantAdmin', 'Agent', 'Customer'];

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<PublicOnly><LoginPage /></PublicOnly>} />
      <Route path="/register" element={<PublicOnly><RegisterPage /></PublicOnly>} />

      <Route element={<RequireAuth><AppShell /></RequireAuth>}>
        <Route index element={<HomeRedirect />} />

        {/* Platform (SuperAdmin) */}
        <Route path="/platform/tenants" element={<RequireAuth roles={['SuperAdmin']}><TenantDirectoryPage /></RequireAuth>} />
        <Route path="/platform/system" element={<RequireAuth roles={['SuperAdmin']}><SystemHealthPage /></RequireAuth>} />

        {/* Staff workspace */}
        <Route path="/dashboard" element={<RequireAuth roles={STAFF}><DashboardPage /></RequireAuth>} />
        <Route path="/tickets" element={<RequireAuth roles={STAFF}><TicketQueuePage /></RequireAuth>} />
        <Route path="/tickets/:id" element={<RequireAuth roles={STAFF}><TicketDetailPage /></RequireAuth>} />
        <Route path="/analytics" element={<RequireAuth roles={STAFF}><AnalyticsPage /></RequireAuth>} />

        {/* Shared by staff and customers */}
        <Route path="/chat" element={<RequireAuth roles={TENANT_USERS}><ChatPage /></RequireAuth>} />
        <Route path="/kb" element={<RequireAuth roles={TENANT_USERS}><KnowledgeBasePage /></RequireAuth>} />

        {/* Tenant administration */}
        <Route path="/admin/sla" element={<RequireAuth roles={ADMIN}><SlaPoliciesPage /></RequireAuth>} />
        <Route path="/admin/automation" element={<RequireAuth roles={ADMIN}><AutomationPage /></RequireAuth>} />
        <Route path="/admin/webhooks" element={<RequireAuth roles={ADMIN}><WebhooksPage /></RequireAuth>} />
        <Route path="/admin/billing" element={<RequireAuth roles={ADMIN}><BillingPage /></RequireAuth>} />
        <Route path="/admin/audit" element={<RequireAuth roles={ADMIN}><AuditLogPage /></RequireAuth>} />

        {/* Customer portal */}
        <Route path="/portal" element={<RequireAuth roles={['Customer']}><PortalPage /></RequireAuth>} />
        <Route path="/portal/tickets/:id" element={<RequireAuth roles={['Customer']}><PortalTicketPage /></RequireAuth>} />
        <Route path="/portal/chat" element={<Navigate to="/chat" replace />} />
        <Route path="/portal/kb" element={<Navigate to="/kb" replace />} />

        <Route path="/account" element={<AccountPage />} />
      </Route>

      <Route path="*" element={<HomeRedirect />} />
    </Routes>
  );
}
