import { lazy, type ReactNode } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { AppShell } from './components/layout/AppShell';
import { homePathFor } from './lib/navigation';
import type { Role } from './lib/types';
import { AccountPage } from './pages/AccountPage';
import { DashboardPage } from './pages/DashboardPage';
import { PlaceholderPage } from './pages/PlaceholderPage';
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
        <Route path="/analytics" element={<RequireAuth roles={STAFF}><PlaceholderPage /></RequireAuth>} />

        {/* Shared by staff and customers */}
        <Route path="/chat" element={<RequireAuth roles={TENANT_USERS}><ChatPage /></RequireAuth>} />
        <Route path="/kb" element={<RequireAuth roles={TENANT_USERS}><KnowledgeBasePage /></RequireAuth>} />

        {/* Tenant administration */}
        {['/admin/sla', '/admin/automation', '/admin/webhooks', '/admin/billing', '/admin/audit'].map((p) => (
          <Route key={p} path={p} element={<RequireAuth roles={ADMIN}><PlaceholderPage /></RequireAuth>} />
        ))}

        {/* Customer portal */}
        <Route path="/portal" element={<RequireAuth roles={['Customer']}><PlaceholderPage /></RequireAuth>} />
        <Route path="/portal/chat" element={<Navigate to="/chat" replace />} />
        <Route path="/portal/kb" element={<Navigate to="/kb" replace />} />

        <Route path="/account" element={<AccountPage />} />
      </Route>

      <Route path="*" element={<HomeRedirect />} />
    </Routes>
  );
}
