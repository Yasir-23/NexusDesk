// Launches Vitest with a normalised working directory.
// On Windows a lowercase drive letter (d:\ vs D:\) makes Vite resolve the same module
// under two IDs, so `vitest` is instantiated twice and suites fail with
// "Vitest failed to find the current suite". Upper-casing the drive letter fixes it.
//
// `--live` runs only the live end-to-end suite against a running backend + dev server
// (target overridable with VITE_API_BASE_URL, default http://localhost:5173).
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { pathToFileURL } from 'node:url';

const cwd = process.cwd();
const normalised = cwd.replace(/^[a-z]:/, (d) => d.toUpperCase());
if (normalised !== cwd) process.chdir(normalised);

const liveIndex = process.argv.indexOf('--live');
if (liveIndex !== -1) {
  process.argv.splice(liveIndex, 1);
  process.env.NEXUSDESK_LIVE = '1';
  process.env.VITE_API_BASE_URL ||= 'http://localhost:5173';
}

const require = createRequire(join(normalised, 'package.json'));
const vitestDir = dirname(require.resolve('vitest/package.json'));
await import(pathToFileURL(join(vitestDir, 'vitest.mjs')).href);
