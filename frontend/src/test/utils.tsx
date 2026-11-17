import { QueryClient } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { vi } from 'vitest';
import { AppProviders } from '../App';
import { AppRoutes } from '../routes';
import { useAuthStore } from '../stores/auth';
import type { AuthUser, LoginResponse } from '../lib/types';

export interface MockCall {
  url: string;
  method: string;
  headers: Headers;
  body: unknown;
}

type Reply = { status?: number; body?: unknown; contentType?: string; headers?: Record<string, string> };
type Handler = (call: MockCall) => Reply | undefined;

/**
 * Installs a `fetch` mock. `routes` maps "METHOD /path" (path without query) to a reply
 * or a handler. Unmatched requests return 404 ProblemDetails. All calls are recorded.
 */
export function mockFetch(routes: Record<string, Reply | Handler>) {
  const calls: MockCall[] = [];
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const method = (init?.method ?? 'GET').toUpperCase();
    const headers = new Headers(init?.headers);
    let body: unknown = init?.body;
    if (typeof body === 'string') {
      try {
        body = JSON.parse(body);
      } catch {
        /* keep raw */
      }
    }
    const call: MockCall = { url, method, headers, body };
    calls.push(call);

    const path = new URL(url, 'http://localhost').pathname;
    const route = routes[`${method} ${path}`];
    const reply: Reply =
      (typeof route === 'function' ? route(call) : route) ?? {
        status: 404,
        body: { title: 'Not Found', status: 404, detail: `No mock for ${method} ${path}` },
        contentType: 'application/problem+json',
      };
    const status = reply.status ?? 200;
    const text = reply.body === undefined ? '' : typeof reply.body === 'string' ? reply.body : JSON.stringify(reply.body);
    return new Response(status === 204 ? null : text, {
      status,
      headers: {
        'Content-Type': reply.contentType ?? (status >= 400 ? 'application/problem+json' : 'application/json'),
        ...reply.headers,
      },
    });
  });
  vi.stubGlobal('fetch', fn);
  return { fn, calls, find: (method: string, path: string) => calls.filter((c) => c.method === method && c.url.includes(path)) };
}

export function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname}</div>;
}

export function renderApp(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <AppProviders queryClient={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
        <LocationProbe />
      </MemoryRouter>
    </AppProviders>,
  );
}

export const USERS: Record<string, AuthUser> = {
  superAdmin: {
    id: '11111111-1111-1111-1111-111111111111',
    email: 'superadmin@nexusdesk.com',
    fullName: 'Platform Super Admin',
    role: 'SuperAdmin',
    tenantId: '00000000-0000-0000-0000-000000000000',
    subdomain: 'platform',
  },
  acmeAdmin: {
    id: '22222222-2222-2222-2222-222222222221',
    email: 'admin@acme.com',
    fullName: 'Acme Admin',
    role: 'TenantAdmin',
    tenantId: 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4',
    subdomain: 'acme',
  },
  acmeAgent: {
    id: '22222222-2222-2222-2222-222222222222',
    email: 'agent@acme.com',
    fullName: 'Acme Agent',
    role: 'Agent',
    tenantId: 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4',
    subdomain: 'acme',
  },
  acmeCustomer: {
    id: '22222222-2222-2222-2222-222222222223',
    email: 'customer@acme.com',
    fullName: 'Acme Customer',
    role: 'Customer',
    tenantId: 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4',
    subdomain: 'acme',
  },
};

export function loginResponse(user: AuthUser): LoginResponse {
  return { accessToken: `token-${user.role}`, tokenType: 'Bearer', expiresIn: 3600, refreshToken: `refresh-${user.role}`, user };
}

export function signInAs(user: AuthUser) {
  useAuthStore.getState().setSession(loginResponse(user), user.subdomain);
}

export const ACME_CURRENT = {
  tenantContext: {
    tenantId: 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4',
    subdomain: 'acme',
    planTier: 'Pro',
    isolationStrategy: 'SharedDatabase',
    status: 'Active',
    isResolved: true,
  },
  activeDatabase: 'nexusdesk_shared',
  rlsSessionTenantId: 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4',
  settingsCount: 1,
  settings: [],
  membershipsCount: 1,
  memberships: [
    {
      id: 'm1',
      userId: '22222222-2222-2222-2222-222222222221',
      role: 'TenantAdmin',
      email: 'admin@acme.com',
      fullName: 'Acme Admin',
      tenantId: 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4',
    },
  ],
};
