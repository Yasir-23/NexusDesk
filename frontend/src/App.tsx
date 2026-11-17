import { QueryClient, QueryClientProvider, useQueryClient, type Query } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { BrowserRouter, useNavigate } from 'react-router-dom';
import { Toaster } from './components/ui';
import { ApiProblemError, setUnauthorizedHandler } from './lib/api';
import { isSameScope, scopeOfKey, sessionScope } from './lib/queries';
import { AppRoutes } from './routes';
import { useAuthStore } from './stores/auth';

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 10_000,
        refetchOnWindowFocus: false,
        // Client errors (4xx) are deterministic; only retry network/5xx once.
        retry: (count, error) => !(error instanceof ApiProblemError && error.status >= 400 && error.status < 500) && count < 1,
      },
    },
  });
}

/** Routes 401 redirects through React Router instead of a full page reload. */
function UnauthorizedBridge() {
  const navigate = useNavigate();
  useEffect(() => {
    setUnauthorizedHandler(() => navigate('/login?expired=1', { replace: true }));
  }, [navigate]);
  return null;
}

/**
 * Whenever the signed-in user or active tenant changes (login, logout, quick account switch,
 * SuperAdmin tenant switch), cancel and evict every query that belongs to another scope.
 * Queries of the new scope are never touched, so pages re-rendering with the new keys fetch
 * immediately, and nothing cached for the previous identity can be shown again.
 */
function SessionScopeSync() {
  const qc = useQueryClient();
  useEffect(() => {
    let current = sessionScope(useAuthStore.getState());
    return useAuthStore.subscribe((state) => {
      const next = sessionScope(state);
      if (isSameScope(next, current)) return;
      current = next;
      const predicate = (q: Query) => {
        const scope = scopeOfKey(q.queryKey);
        return !scope || !isSameScope(scope, next);
      };
      void qc.cancelQueries({ predicate });
      qc.removeQueries({ predicate });
    });
  }, [qc]);
  return null;
}

export function AppProviders({ children, queryClient }: { children: ReactNode; queryClient?: QueryClient }) {
  const [client] = useState(() => queryClient ?? createQueryClient());
  return (
    <QueryClientProvider client={client}>
      <SessionScopeSync />
      {children}
      <Toaster />
    </QueryClientProvider>
  );
}

export default function App() {
  return (
    <AppProviders>
      <BrowserRouter>
        <UnauthorizedBridge />
        <AppRoutes />
      </BrowserRouter>
    </AppProviders>
  );
}
