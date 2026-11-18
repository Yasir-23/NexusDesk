import { Menu, Moon, Sun, X } from 'lucide-react';
import { Suspense, useEffect, useState, type ReactNode } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { AccountMenu } from './AccountMenu';
import { Logo } from './Logo';
import { NotificationBell } from './NotificationBell';
import { RealtimeBridge, RealtimeIndicator } from './Realtime';
import { TenantContextBadge } from './TenantContextBadge';
import { navFor } from '../../lib/navigation';
import { useAuthStore } from '../../stores/auth';
import { useThemeStore } from '../../stores/theme';
import { cn } from '../../lib/cn';

function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const role = useAuthStore((s) => s.user?.role);
  if (!role) return null;

  return (
    <nav aria-label="Main navigation" className="flex h-full flex-col">
      <div className="flex h-14 items-center px-5">
        <Logo />
      </div>
      <div className="flex-1 space-y-6 overflow-y-auto px-3 py-4">
        {navFor(role).map((section) => (
          <div key={section.title}>
            <p className="px-2.5 pb-1.5 text-[11px] font-semibold uppercase tracking-wider text-zinc-400">{section.title}</p>
            <ul className="space-y-0.5">
              {section.items.map((item) => (
                <li key={item.to}>
                  <NavLink
                    to={item.to}
                    onClick={onNavigate}
                    className={({ isActive }) =>
                      cn(
                        'group flex items-center gap-2.5 rounded-lg px-2.5 py-1.5 text-sm transition-colors',
                        isActive
                          ? 'bg-white font-medium text-zinc-900 shadow-sm ring-1 ring-zinc-200 dark:bg-zinc-800 dark:text-zinc-100 dark:ring-zinc-700'
                          : 'text-zinc-600 hover:bg-zinc-200/50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/60 dark:hover:text-zinc-100',
                      )
                    }
                  >
                    <item.icon className="h-4 w-4 shrink-0" aria-hidden />
                    <span className="flex-1 truncate">{item.label}</span>
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      <div className="border-t border-zinc-200 px-5 py-3 text-[11px] text-zinc-400 dark:border-zinc-800">
        NexusDesk · .NET 9 multi-tenant SaaS
      </div>
    </nav>
  );
}

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const { theme, toggle } = useThemeStore();
  const location = useLocation();

  useEffect(() => setMobileOpen(false), [location.pathname]);

  return (
    <div className="min-h-dvh">
      <RealtimeBridge />
      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 hidden w-60 border-r border-zinc-200 bg-zinc-100/60 lg:block dark:border-zinc-800 dark:bg-zinc-900/40">
        <Sidebar />
      </aside>

      {/* Mobile sidebar */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-zinc-950/40" onClick={() => setMobileOpen(false)} aria-hidden />
          <aside className="animate-fade-in absolute inset-y-0 left-0 w-64 border-r border-zinc-200 bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-950">
            <button
              type="button"
              onClick={() => setMobileOpen(false)}
              className="absolute right-3 top-3.5 rounded-md p-1 text-zinc-400"
              aria-label="Close navigation"
            >
              <X className="h-5 w-5" />
            </button>
            <Sidebar onNavigate={() => setMobileOpen(false)} />
          </aside>
        </div>
      )}

      <div className="lg:pl-60">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-zinc-200 bg-white/80 px-4 backdrop-blur sm:px-6 dark:border-zinc-800 dark:bg-zinc-950/80">
          <button
            type="button"
            onClick={() => setMobileOpen(true)}
            className="rounded-md p-1.5 text-zinc-500 hover:bg-zinc-100 lg:hidden dark:hover:bg-zinc-800"
            aria-label="Open navigation"
          >
            <Menu className="h-5 w-5" />
          </button>
          <div className="min-w-0">
            <TenantContextBadge />
          </div>
          <div className="ml-auto flex items-center gap-1">
            <RealtimeIndicator />
            <NotificationBell />
            <button
              type="button"
              onClick={toggle}
              className="rounded-lg p-2 text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:hover:bg-zinc-800 dark:hover:text-zinc-100"
              aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
            >
              {theme === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
            </button>
            <AccountMenu />
          </div>
        </header>
        <main className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          <Suspense fallback={<PageFallback />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  );
}

function PageFallback() {
  return (
    <div className="space-y-4" aria-busy="true" aria-label="Loading page">
      <div className="h-7 w-48 animate-pulse rounded-md bg-zinc-200/70 dark:bg-zinc-800" />
      <div className="h-64 w-full animate-pulse rounded-xl bg-zinc-200/70 dark:bg-zinc-800" />
    </div>
  );
}

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold tracking-tight text-zinc-900 dark:text-zinc-50">{title}</h1>
        {description && <p className="mt-1 text-sm text-zinc-500 dark:text-zinc-400">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
