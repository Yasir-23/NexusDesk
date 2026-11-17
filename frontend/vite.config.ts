/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// Every backend path the SPA talks to. In dev, these are proxied to the .NET API so the
// browser only ever sees one origin (localhost:5173 or a single ngrok/tunnel URL).
const BACKEND_PATHS = ['/api', '/connect', '/health', '/hubs', '/graphql', '/swagger'];

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const proxyTarget = env.VITE_PROXY_TARGET || 'http://localhost:56100';
  // `npm run test:live` sets this to run only the live end-to-end suite.
  const live = process.env.NEXUSDESK_LIVE === '1';

  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 5173,
      strictPort: true,
      // Allow tunnel hostnames (ngrok, Cloudflare) to reach the dev server.
      allowedHosts: ['localhost', '.ngrok-free.app', '.ngrok.app', '.ngrok.io', '.trycloudflare.com', '.nexusdesk.local'],
      proxy: Object.fromEntries(
        BACKEND_PATHS.map((path) => [path, { target: proxyTarget, changeOrigin: true, ws: path === '/hubs' }]),
      ),
    },
    preview: { port: 4173 },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      include: live ? ['src/**/*.live.test.{ts,tsx}'] : ['src/**/*.test.{ts,tsx}'],
      exclude: live ? ['node_modules/**'] : ['node_modules/**', 'src/**/*.live.test.{ts,tsx}'],
      css: false,
      restoreMocks: true,
      // userEvent-driven tests take ~2s alone but ~3x longer when all suites run in
      // parallel, which overruns Vitest's 5s default.
      testTimeout: 15_000,
      // Node 25+ ships a global `localStorage` that shadows jsdom's and is unusable without
      // --localstorage-file. Disable it so tests use jsdom's Web Storage.
      execArgv: ['--no-experimental-webstorage'],
    },
  };
});
