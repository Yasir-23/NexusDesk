import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useCallback, useEffect, useRef } from 'react';
import { create } from 'zustand';
import { API_BASE_URL } from './api';
import { useAuthStore, type AuthState } from '../stores/auth';

/**
 * Real-time connection manager for `/hubs/tickets` and `/hubs/chat` (contract §4).
 *
 * - One long-lived connection per hub, started while a tenant user is signed in.
 * - Torn down and rebuilt whenever the access token, tenant or user changes, so the hub
 *   always authenticates as the current identity.
 * - Event handlers and joined rooms are kept in registries that outlive any single
 *   connection: after an automatic reconnect or a rebuild, handlers are re-attached and
 *   `Join*` calls are replayed, so pages never have to resubscribe.
 */

export type HubName = 'tickets' | 'chat';
export type RealtimeStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/** The slice of `@microsoft/signalr`'s `HubConnection` we rely on (lets tests inject a fake). */
export interface HubConnectionLike {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(event: string, handler: (...args: unknown[]) => void): void;
  off(event: string, handler: (...args: unknown[]) => void): void;
  invoke(method: string, ...args: unknown[]): Promise<unknown>;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

export interface HubConnectionOptions {
  url: string;
  tenant: string | null;
  accessTokenFactory: () => string;
}

export type HubConnectionFactory = (hub: HubName, options: HubConnectionOptions) => HubConnectionLike;

const defaultFactory: HubConnectionFactory = (_hub, { url, tenant, accessTokenFactory }) =>
  new HubConnectionBuilder()
    .withUrl(url, {
      accessTokenFactory,
      // Applied to negotiate / long-polling requests; the hubs also resolve the tenant from the JWT.
      headers: tenant ? { 'X-Tenant-Subdomain': tenant } : undefined,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

let connectionFactory: HubConnectionFactory = defaultFactory;

/** Swap the connection factory (tests). `null` restores the real SignalR client. */
export function setHubConnectionFactory(factory: HubConnectionFactory | null) {
  connectionFactory = factory ?? defaultFactory;
}

// ---------------------------------------------------------------------------
// Status store
// ---------------------------------------------------------------------------

export const useRealtimeStore = create<Record<HubName, RealtimeStatus>>()(() => ({ tickets: 'idle', chat: 'idle' }));

// ---------------------------------------------------------------------------
// Hub manager
// ---------------------------------------------------------------------------

type Handler = (payload: never) => void;

interface Room {
  join: string;
  leave: string;
  args: unknown[];
}

const RETRY_MS = 5_000;

export class HubManager {
  private connection: HubConnectionLike | null = null;
  private connected = false;
  private sessionKey: string | null = null;
  /** Bumped on every start/stop so callbacks of a superseded connection are ignored. */
  private generation = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly handlers = new Map<string, Set<Handler>>();
  private readonly dispatchers = new Map<string, (...args: unknown[]) => void>();
  private readonly rooms = new Set<Room>();

  constructor(
    readonly name: HubName,
    readonly path: string,
  ) {}

  private setStatus(status: RealtimeStatus) {
    useRealtimeStore.setState({ [this.name]: status });
  }

  private dispatcher(event: string) {
    let d = this.dispatchers.get(event);
    if (!d) {
      d = (payload: unknown) => this.handlers.get(event)?.forEach((h) => (h as (p: unknown) => void)(payload));
      this.dispatchers.set(event, d);
    }
    return d;
  }

  /** Subscribe to a server → client event. Survives reconnects. Returns an unsubscribe. */
  on<T>(event: string, handler: (payload: T) => void): () => void {
    let set = this.handlers.get(event);
    if (!set) {
      set = new Set();
      this.handlers.set(event, set);
      this.connection?.on(event, this.dispatcher(event));
    }
    set.add(handler as Handler);
    return () => {
      set.delete(handler as Handler);
    };
  }

  /** Invoke a client → server method; a no-op while disconnected. Errors are swallowed. */
  async invoke(method: string, ...args: unknown[]): Promise<void> {
    if (!this.connection || !this.connected) return;
    try {
      await this.connection.invoke(method, ...args);
    } catch {
      /* best-effort: presence/typing are advisory */
    }
  }

  /** Joins a server group now (if connected) and again after every reconnect, until released. */
  joinRoom(join: string, leave: string, ...args: unknown[]): () => void {
    const room: Room = { join, leave, args };
    this.rooms.add(room);
    void this.invoke(join, ...args);
    return () => {
      if (!this.rooms.delete(room)) return;
      void this.invoke(leave, ...args);
    };
  }

  private rejoinRooms() {
    for (const room of this.rooms) void this.invoke(room.join, ...room.args);
  }

  /** Starts (or restarts, if `key` changed) the connection for the given identity. */
  start(key: string, options: Omit<HubConnectionOptions, 'url'>) {
    if (key === this.sessionKey && this.connection) return;
    this.teardown();
    this.sessionKey = key;
    const generation = ++this.generation;
    const connection = connectionFactory(this.name, { ...options, url: `${API_BASE_URL}${this.path}` });
    this.connection = connection;

    for (const event of this.handlers.keys()) connection.on(event, this.dispatcher(event));
    connection.onreconnecting(() => {
      if (generation !== this.generation) return;
      this.connected = false;
      this.setStatus('reconnecting');
    });
    connection.onreconnected(() => {
      if (generation !== this.generation) return;
      this.connected = true;
      this.setStatus('connected');
      this.rejoinRooms();
    });
    connection.onclose(() => {
      if (generation !== this.generation) return;
      this.connected = false;
      this.setStatus('disconnected');
      this.scheduleRetry(key, options, generation);
    });

    this.setStatus('connecting');
    connection.start().then(
      () => {
        if (generation !== this.generation) return;
        this.connected = true;
        this.setStatus('connected');
        this.rejoinRooms();
      },
      () => {
        if (generation !== this.generation) return;
        this.setStatus('disconnected');
        this.scheduleRetry(key, options, generation);
      },
    );
  }

  private scheduleRetry(key: string, options: Omit<HubConnectionOptions, 'url'>, generation: number) {
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      if (generation !== this.generation) return;
      this.sessionKey = null; // force a fresh connection
      this.start(key, options);
    }, RETRY_MS);
  }

  private teardown() {
    this.generation++;
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = null;
    const old = this.connection;
    this.connection = null;
    this.connected = false;
    this.sessionKey = null;
    if (old) void old.stop().catch(() => undefined);
  }

  /** Stops the connection (sign-out / platform scope). Handlers and rooms are kept. */
  stop() {
    this.teardown();
    this.setStatus('idle');
  }

  get isConnected() {
    return this.connected;
  }
}

export const hubs: Record<HubName, HubManager> = {
  tickets: new HubManager('tickets', '/hubs/tickets'),
  chat: new HubManager('chat', '/hubs/chat'),
};

// ---------------------------------------------------------------------------
// Auth wiring
// ---------------------------------------------------------------------------

function connectionKey(s: Pick<AuthState, 'accessToken' | 'tenantSubdomain' | 'user'>): string | null {
  // Hubs are tenant-scoped: only connect for a signed-in user with an active tenant.
  if (!s.accessToken || !s.user || !s.tenantSubdomain) return null;
  return `${s.user.id}|${s.tenantSubdomain}|${s.accessToken}`;
}

function sync(state: AuthState) {
  const key = connectionKey(state);
  for (const hub of Object.values(hubs)) {
    if (!key) hub.stop();
    else
      hub.start(key, {
        tenant: state.tenantSubdomain,
        accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
      });
  }
}

/** Keeps both hubs in sync with the auth store. Returns a disposer that stops them. */
export function startRealtimeSync(): () => void {
  sync(useAuthStore.getState());
  const unsubscribe = useAuthStore.subscribe((state, prev) => {
    if (connectionKey(state) !== connectionKey(prev)) sync(state);
  });
  return () => {
    unsubscribe();
    for (const hub of Object.values(hubs)) hub.stop();
  };
}

// ---------------------------------------------------------------------------
// React hooks
// ---------------------------------------------------------------------------

/** Subscribes to a hub event for the component's lifetime (latest handler always used). */
export function useHubEvent<T>(hub: HubName, event: string, handler: (payload: T) => void) {
  const ref = useRef(handler);
  useEffect(() => {
    ref.current = handler;
  });
  useEffect(() => hubs[hub].on<T>(event, (payload) => ref.current(payload)), [hub, event]);
}

/** Joins a hub room (e.g. `JoinTicketRoom(ticketId)`) while mounted and `id` is set. */
export function useHubRoom(hub: HubName, join: string, leave: string, id: string | null | undefined) {
  useEffect(() => {
    if (!id) return;
    return hubs[hub].joinRoom(join, leave, id);
  }, [hub, join, leave, id]);
}

/**
 * Debounced typing presence: `ping(hasText)` on every keystroke sends `method(id, true)` once,
 * then `method(id, false)` after `idleMs` of inactivity, on `stop()`, or when unmounting.
 */
export function useTypingSignal(hub: HubName, method: string, id: string | null | undefined, idleMs = 2_500) {
  const typing = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const send = useCallback(
    (value: boolean) => {
      if (!id || typing.current === value) return;
      typing.current = value;
      void hubs[hub].invoke(method, id, value);
    },
    [hub, method, id],
  );

  const stop = useCallback(() => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
    send(false);
  }, [send]);

  const ping = useCallback(
    (hasText: boolean) => {
      if (!hasText) return stop();
      send(true);
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(stop, idleMs);
    },
    [send, stop, idleMs],
  );

  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
      timer.current = null;
      if (typing.current && id) void hubs[hub].invoke(method, id, false);
      typing.current = false;
    },
    [hub, method, id],
  );

  return { ping, stop };
}

export function useRealtimeStatus(hub: HubName): RealtimeStatus {
  return useRealtimeStore((s) => s[hub]);
}
