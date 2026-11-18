import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { useAuthStore } from '../stores/auth';
import { resetToasts } from '../stores/toast';
import { installFakeHubs, resetFakeHubs } from './fakeHub';

// Unit tests never open real SignalR connections: a fake hub records invocations and lets
// tests emit server events. The live suite (`npm run test:live`) uses the real client.
const LIVE = process.env.NEXUSDESK_LIVE === '1';

// Route pages are lazy chunks. With every suite transforming in parallel, the first chunk of a
// file can take more than 1s to load, which made the default 1s `findBy*` timeout flaky.
// 5s is still well under the 15s test timeout.
configure({ asyncUtilTimeout: 5_000 });

beforeEach(() => {
  if (!LIVE) installFakeHubs();
});

afterEach(() => {
  cleanup();
  if (!LIVE) resetFakeHubs();
  localStorage.clear();
  useAuthStore.setState({ accessToken: null, refreshToken: null, expiresAt: null, user: null, tenantSubdomain: null });
  resetToasts();
  vi.unstubAllGlobals();
});
