import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';
import { useAuthStore } from '../stores/auth';
import { resetToasts } from '../stores/toast';

afterEach(() => {
  cleanup();
  localStorage.clear();
  useAuthStore.setState({ accessToken: null, refreshToken: null, expiresAt: null, user: null, tenantSubdomain: null });
  resetToasts();
  vi.unstubAllGlobals();
});
