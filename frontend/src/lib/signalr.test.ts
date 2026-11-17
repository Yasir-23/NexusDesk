import { afterEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '../stores/auth';
import { fakeConnections, hubConnection } from '../test/fakeHub';
import { USERS, signInAs } from '../test/utils';
import { hubs, startRealtimeSync, useRealtimeStore } from './signalr';

const flush = () => new Promise((r) => setTimeout(r, 0));

describe('SignalR connection manager', () => {
  let dispose: (() => void) | undefined;
  afterEach(() => dispose?.());

  it('connects both hubs for a tenant user with the bearer token factory and tenant header', async () => {
    signInAs(USERS.acmeAgent);
    dispose = startRealtimeSync();
    await flush();

    expect(useRealtimeStore.getState()).toEqual({ tickets: 'connected', chat: 'connected' });
    const t = hubConnection('tickets');
    expect(t.options.url).toMatch(/\/hubs\/tickets$/);
    expect(t.options.tenant).toBe('acme');
    expect(t.options.accessTokenFactory()).toBe('token-Agent');
  });

  it('stays idle without a tenant (SuperAdmin platform scope)', async () => {
    signInAs(USERS.superAdmin);
    dispose = startRealtimeSync();
    await flush();
    expect(fakeConnections.tickets).toHaveLength(0);
    expect(useRealtimeStore.getState().tickets).toBe('idle');
  });

  it('tears down and rebuilds when the access token or tenant changes', async () => {
    signInAs(USERS.acmeAgent);
    dispose = startRealtimeSync();
    await flush();
    const first = hubConnection('tickets');

    useAuthStore.getState().setTokens('rotated-token', null, 3600);
    await flush();
    expect(first.stopped).toBe(true);
    const second = hubConnection('tickets');
    expect(second).not.toBe(first);
    expect(second.options.accessTokenFactory()).toBe('rotated-token');

    useAuthStore.getState().setTenantSubdomain('globex');
    await flush();
    expect(second.stopped).toBe(true);
    expect(hubConnection('tickets').options.tenant).toBe('globex');

    useAuthStore.getState().logout();
    await flush();
    expect(fakeConnections.tickets.every((c) => c.stopped)).toBe(true);
    expect(useRealtimeStore.getState().tickets).toBe('idle');
  });

  it('keeps handlers and replays room joins across reconnects and rebuilds', async () => {
    signInAs(USERS.acmeAgent);
    const handler = vi.fn();
    const off = hubs.tickets.on('TicketUpdated', handler);
    const leave = hubs.tickets.joinRoom('JoinTicketRoom', 'LeaveTicketRoom', 'ticket-1');
    dispose = startRealtimeSync();
    await flush();

    const c1 = hubConnection('tickets');
    expect(c1.calls('JoinTicketRoom')).toEqual([['ticket-1']]);

    c1.simulateReconnecting();
    expect(useRealtimeStore.getState().tickets).toBe('reconnecting');
    c1.simulateReconnected();
    expect(useRealtimeStore.getState().tickets).toBe('connected');
    expect(c1.calls('JoinTicketRoom')).toEqual([['ticket-1'], ['ticket-1']]);

    useAuthStore.getState().setTokens('rotated', null, 3600);
    await flush();
    const c2 = hubConnection('tickets');
    expect(c2.calls('JoinTicketRoom')).toEqual([['ticket-1']]);
    c2.emit('TicketUpdated', { ticketId: 'ticket-1' });
    expect(handler).toHaveBeenCalledWith({ ticketId: 'ticket-1' });

    leave();
    expect(c2.calls('LeaveTicketRoom')).toEqual([['ticket-1']]);
    c2.simulateReconnected();
    expect(c2.calls('JoinTicketRoom')).toHaveLength(1); // released rooms are not rejoined

    off();
    c2.emit('TicketUpdated', { ticketId: 'ticket-1' });
    expect(handler).toHaveBeenCalledTimes(1);
  });
});
