import { useAuthStore } from '../stores/auth';
import { toast } from '../stores/toast';
import type { OAuthTokenResponse, ProblemDetails } from './types';

/**
 * Absolute API origin. Empty (the default) means same-origin requests through the Vite
 * proxy, which is what lets `localhost:5173` and a single ngrok/tunnel URL both work.
 */
export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export const OAUTH_CLIENT_ID = 'nexusdesk-spa';

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

/** A typed RFC 7807 `ProblemDetails` error raised for every non-2xx response. */
export class ApiProblemError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string | undefined;
  /** Machine-readable business code (`code` or `errorCode`), e.g. `TENANT_SUSPENDED`. */
  readonly code: string | undefined;
  readonly type: string | undefined;
  readonly instance: string | undefined;
  readonly traceId: string | undefined;
  readonly validationErrors: Record<string, string[]> | undefined;
  readonly problem: ProblemDetails;

  constructor(problem: ProblemDetails & { status: number; title: string }) {
    super(problem.detail || problem.title);
    this.name = 'ApiProblemError';
    this.status = problem.status;
    this.title = problem.title;
    this.detail = problem.detail;
    this.code = problem.code ?? problem.errorCode;
    this.type = problem.type;
    this.instance = problem.instance;
    this.traceId = problem.traceId;
    this.validationErrors = problem.errors;
    this.problem = problem;
  }

  /** Best single sentence to show a user. */
  get userMessage(): string {
    if (this.detail) return this.detail;
    const firstValidation = this.validationErrors && Object.values(this.validationErrors).flat()[0];
    return firstValidation || this.title;
  }
}

const STATUS_TITLES: Record<number, string> = {
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  409: 'Conflict',
  422: 'Unprocessable Entity',
  429: 'Too Many Requests',
  500: 'Server Error',
  502: 'Bad Gateway',
  503: 'Service Unavailable',
};

/**
 * Normalises any error body into an `ApiProblemError`. Accepts a parsed ProblemDetails
 * object, a plain string body, or nothing at all.
 */
export function parseProblemDetails(status: number, body: unknown, statusText = ''): ApiProblemError {
  const fallbackTitle = STATUS_TITLES[status] ?? (statusText || `HTTP ${status}`);

  if (body && typeof body === 'object' && !Array.isArray(body)) {
    const p = body as ProblemDetails & Record<string, unknown>;
    const asString = (v: unknown) => (typeof v === 'string' && v.trim() ? v : undefined);
    const errors = isValidationErrors(p.errors) ? p.errors : undefined;
    let detail = asString(p.detail);
    if (!detail && errors) detail = Object.values(errors).flat().join(' ');
    return new ApiProblemError({
      type: asString(p.type),
      title: asString(p.title) ?? fallbackTitle,
      status: typeof p.status === 'number' ? p.status : status,
      detail,
      instance: asString(p.instance),
      code: asString(p.code),
      errorCode: asString(p.errorCode),
      traceId: asString(p.traceId),
      errors,
    });
  }

  const text = typeof body === 'string' && body.trim() ? body.trim().slice(0, 300) : undefined;
  return new ApiProblemError({ title: fallbackTitle, status, detail: text });
}

function isValidationErrors(v: unknown): v is Record<string, string[]> {
  return !!v && typeof v === 'object' && Object.values(v).every((x) => Array.isArray(x));
}

// ---------------------------------------------------------------------------
// Unauthorized handling
// ---------------------------------------------------------------------------

type UnauthorizedHandler = () => void;
let onUnauthorized: UnauthorizedHandler = () => {
  if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/login')) {
    window.location.assign('/login?expired=1');
  }
};

/** Lets the router own the post-logout redirect (avoids a full page reload). */
export function setUnauthorizedHandler(handler: UnauthorizedHandler) {
  onUnauthorized = handler;
}

// ---------------------------------------------------------------------------
// Core request
// ---------------------------------------------------------------------------

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  query?: Record<string, string | number | boolean | null | undefined>;
  /**
   * Tenant scope. `undefined` (default) uses the active tenant from the auth store,
   * a string overrides it for this call, and `null` sends no tenant header.
   */
  tenant?: string | null;
  /** Attach the bearer token (default `true`). `false` also disables 401 refresh/redirect. */
  auth?: boolean;
  /** Suppress the automatic error toast. */
  silent?: boolean;
  signal?: AbortSignal;
  headers?: Record<string, string>;
}

export function buildUrl(path: string, query?: RequestOptions['query']): string {
  let url = `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;
  if (query) {
    const params = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) {
      if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
    }
    const qs = params.toString();
    if (qs) url += `?${qs}`;
  }
  return url;
}

export function buildHeaders(opts: Pick<RequestOptions, 'tenant' | 'auth' | 'headers' | 'body'>): Headers {
  const { accessToken, tenantSubdomain } = useAuthStore.getState();
  const headers = new Headers({ Accept: 'application/json, application/problem+json' });

  const tenant = opts.tenant === undefined ? tenantSubdomain : opts.tenant;
  if (tenant) headers.set('X-Tenant-Subdomain', tenant);

  if (opts.auth !== false && accessToken) headers.set('Authorization', `Bearer ${accessToken}`);

  if (opts.body !== undefined && !(opts.body instanceof FormData)) headers.set('Content-Type', 'application/json');

  for (const [k, v] of Object.entries(opts.headers ?? {})) headers.set(k, v);
  return headers;
}

async function readBody(res: Response): Promise<unknown> {
  if (res.status === 204) return undefined;
  const text = await res.text();
  if (!text) return undefined;
  const type = res.headers.get('content-type') ?? '';
  if (type.includes('json')) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }
  return text;
}

function send(path: string, opts: RequestOptions): Promise<Response> {
  const body = opts.body === undefined ? undefined : opts.body instanceof FormData ? opts.body : JSON.stringify(opts.body);
  return fetch(buildUrl(path, opts.query), {
    method: opts.method ?? 'GET',
    headers: buildHeaders(opts),
    body,
    signal: opts.signal,
  });
}

export async function apiRequest<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  let res: Response;
  try {
    res = await send(path, opts);
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') throw err;
    const networkError = new ApiProblemError({
      status: 0,
      title: 'Network error',
      detail: 'Could not reach the NexusDesk API. Is the backend running on port 56100?',
    });
    if (!opts.silent) notifyError(networkError);
    throw networkError;
  }

  // Expired access token: try one refresh, then replay the request.
  if (res.status === 401 && opts.auth !== false && useAuthStore.getState().accessToken) {
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      res = await send(path, opts);
    }
    if (res.status === 401) {
      useAuthStore.getState().logout();
      toast.warning('Session expired', 'Please sign in again.');
      onUnauthorized();
      throw parseProblemDetails(401, await readBody(res), res.statusText);
    }
  }

  const data = await readBody(res);
  if (!res.ok) {
    const error = parseProblemDetails(res.status, data, res.statusText);
    if (!opts.silent) notifyError(error);
    throw error;
  }
  return data as T;
}

export function notifyError(error: ApiProblemError) {
  const code = error.code ? `${error.code} · ` : '';
  toast.error(error.title, error.detail ?? error.userMessage, `${code}HTTP ${error.status || 'network'}`);
}

// ---------------------------------------------------------------------------
// Refresh token (`POST /connect/token`, grant_type=refresh_token)
// ---------------------------------------------------------------------------

let refreshInFlight: Promise<boolean> | null = null;

export function refreshAccessToken(): Promise<boolean> {
  if (!refreshInFlight) {
    refreshInFlight = doRefresh().finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

async function doRefresh(): Promise<boolean> {
  const { refreshToken, tenantSubdomain } = useAuthStore.getState();
  if (!refreshToken) return false;
  try {
    const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'application/json' };
    if (tenantSubdomain) headers['X-Tenant-Subdomain'] = tenantSubdomain;
    const res = await fetch(buildUrl('/connect/token'), {
      method: 'POST',
      headers,
      body: JSON.stringify({ grant_type: 'refresh_token', refresh_token: refreshToken, client_id: OAUTH_CLIENT_ID }),
    });
    if (!res.ok) return false;
    const token = (await res.json()) as OAuthTokenResponse;
    if (!token.access_token) return false;
    useAuthStore.getState().setTokens(token.access_token, token.refresh_token ?? null, token.expires_in);
    return true;
  } catch {
    return false;
  }
}

// ---------------------------------------------------------------------------
// Convenience verbs
// ---------------------------------------------------------------------------

type VerbOptions = Omit<RequestOptions, 'method' | 'body'>;

export const api = {
  get: <T>(path: string, opts?: VerbOptions) => apiRequest<T>(path, { ...opts, method: 'GET' }),
  post: <T>(path: string, body?: unknown, opts?: VerbOptions) => apiRequest<T>(path, { ...opts, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, opts?: VerbOptions) => apiRequest<T>(path, { ...opts, method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown, opts?: VerbOptions) => apiRequest<T>(path, { ...opts, method: 'PATCH', body }),
  delete: <T>(path: string, opts?: VerbOptions) => apiRequest<T>(path, { ...opts, method: 'DELETE' }),
};

// ---------------------------------------------------------------------------
// Health probes return 200/503 with arbitrary bodies, so they get a raw helper.
// ---------------------------------------------------------------------------

export interface ProbeResult {
  ok: boolean;
  status: number;
  latencyMs: number;
  body: unknown;
}

export async function probe(path: string, signal?: AbortSignal): Promise<ProbeResult> {
  const started = performance.now();
  try {
    const res = await fetch(buildUrl(path), { headers: { Accept: 'application/json, text/plain' }, signal });
    const body = await readBody(res);
    return { ok: res.ok, status: res.status, latencyMs: Math.round(performance.now() - started), body };
  } catch {
    return { ok: false, status: 0, latencyMs: Math.round(performance.now() - started), body: 'Unreachable' };
  }
}
