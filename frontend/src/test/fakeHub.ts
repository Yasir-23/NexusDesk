import { act } from '@testing-library/react';
import { hubs, setHubConnectionFactory, type HubConnectionLike, type HubConnectionOptions, type HubName } from '../lib/signalr';

type Fn = (...args: unknown[]) => void;

/** In-memory stand-in for a SignalR `HubConnection`: records invocations, lets tests emit events. */
export class FakeHubConnection implements HubConnectionLike {
  readonly handlers = new Map<string, Set<Fn>>();
  readonly invocations: { method: string; args: unknown[] }[] = [];
  started = false;
  stopped = false;
  private reconnecting: ((error?: Error) => void)[] = [];
  private reconnected: ((connectionId?: string) => void)[] = [];
  private closed: ((error?: Error) => void)[] = [];

  constructor(
    readonly hub: HubName,
    readonly options: HubConnectionOptions,
  ) {}

  async start() {
    this.started = true;
  }
  async stop() {
    this.stopped = true;
  }
  on(event: string, handler: Fn) {
    if (!this.handlers.has(event)) this.handlers.set(event, new Set());
    this.handlers.get(event)!.add(handler);
  }
  off(event: string, handler: Fn) {
    this.handlers.get(event)?.delete(handler);
  }
  async invoke(method: string, ...args: unknown[]) {
    this.invocations.push({ method, args });
  }
  onreconnecting(cb: (error?: Error) => void) {
    this.reconnecting.push(cb);
  }
  onreconnected(cb: (connectionId?: string) => void) {
    this.reconnected.push(cb);
  }
  onclose(cb: (error?: Error) => void) {
    this.closed.push(cb);
  }

  emit(event: string, payload: unknown) {
    this.handlers.get(event)?.forEach((h) => h(payload));
  }
  simulateReconnecting() {
    this.reconnecting.forEach((cb) => cb());
  }
  simulateReconnected() {
    this.reconnected.forEach((cb) => cb('new-connection'));
  }
  calls(method: string) {
    return this.invocations.filter((i) => i.method === method).map((i) => i.args);
  }
}

export const fakeConnections: Record<HubName, FakeHubConnection[]> = { tickets: [], chat: [] };

export function installFakeHubs() {
  fakeConnections.tickets = [];
  fakeConnections.chat = [];
  setHubConnectionFactory((hub, options) => {
    const c = new FakeHubConnection(hub, options);
    fakeConnections[hub].push(c);
    return c;
  });
}

export function resetFakeHubs() {
  for (const hub of Object.values(hubs)) hub.stop();
  setHubConnectionFactory(null);
}

/** The live (most recent, not stopped) fake connection for a hub. */
export function hubConnection(hub: HubName): FakeHubConnection {
  const live = fakeConnections[hub].filter((c) => !c.stopped);
  const c = live.at(-1);
  if (!c) throw new Error(`No active fake ${hub} hub connection`);
  return c;
}

/** Emits a server → client hub event inside `act`. */
export async function emitHub(hub: HubName, event: string, payload: unknown) {
  await act(async () => {
    hubConnection(hub).emit(event, payload);
  });
}
