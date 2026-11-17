import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import type { AuthUser, LoginResponse } from '../lib/types';

export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  /** Epoch ms when the access token expires. */
  expiresAt: number | null;
  user: AuthUser | null;
  /**
   * Active tenant subdomain sent as `X-Tenant-Subdomain`. `null` means platform scope
   * (SuperAdmin with no tenant selected).
   */
  tenantSubdomain: string | null;

  setSession: (response: LoginResponse, fallbackSubdomain: string | null) => void;
  setTokens: (accessToken: string, refreshToken: string | null, expiresIn: number) => void;
  setTenantSubdomain: (subdomain: string | null) => void;
  logout: () => void;
}

const emptySession = {
  accessToken: null,
  refreshToken: null,
  expiresAt: null,
  user: null,
} satisfies Partial<AuthState>;

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      ...emptySession,
      tenantSubdomain: null,

      setSession: (response, fallbackSubdomain) =>
        set({
          accessToken: response.accessToken,
          refreshToken: response.refreshToken,
          expiresAt: Date.now() + response.expiresIn * 1000,
          user: response.user,
          // SuperAdmin logs in with `subdomain: "platform"` and an all-zero tenantId:
          // that is platform scope, so no X-Tenant-Subdomain header is sent.
          tenantSubdomain:
            response.user.role === 'SuperAdmin'
              ? null
              : (normalizeSubdomain(response.user.subdomain) ?? normalizeSubdomain(fallbackSubdomain)),
        }),

      setTokens: (accessToken, refreshToken, expiresIn) =>
        set((s) => ({
          accessToken,
          refreshToken: refreshToken ?? s.refreshToken,
          expiresAt: Date.now() + expiresIn * 1000,
        })),

      setTenantSubdomain: (subdomain) => set({ tenantSubdomain: normalizeSubdomain(subdomain) }),

      // Keep the last tenant so the login form remembers it.
      logout: () => set({ ...emptySession }),
    }),
    {
      name: 'nexusdesk-auth',
      storage: createJSONStorage(() => localStorage),
      partialize: (s) => ({
        accessToken: s.accessToken,
        refreshToken: s.refreshToken,
        expiresAt: s.expiresAt,
        user: s.user,
        tenantSubdomain: s.tenantSubdomain,
      }),
    },
  ),
);

export function normalizeSubdomain(value: string | null | undefined): string | null {
  const v = value?.trim().toLowerCase();
  return v ? v : null;
}

export const selectIsAuthenticated = (s: AuthState) => Boolean(s.accessToken && s.user);
