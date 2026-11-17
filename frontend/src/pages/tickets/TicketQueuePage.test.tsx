import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { CreateTicketRequest } from '../../lib/types';
import { emitHub, hubConnection } from '../../test/fakeHub';
import { AGENT_ID, CUSTOMER_ID, QUEUE, TENANT_USERS, paged, ticketDetails } from '../../test/fixtures';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

function mockQueue() {
  return mockFetch({
    'GET /api/v1/tickets': (call) => {
      const q = new URL(call.url, 'http://localhost').searchParams;
      let items = QUEUE;
      if (q.get('status')) items = items.filter((t) => t.status === q.get('status'));
      if (q.get('priority')) items = items.filter((t) => t.priority === q.get('priority'));
      if (q.get('assignedAgentId')) items = items.filter((t) => t.assignedAgentId === q.get('assignedAgentId'));
      return { body: paged(items) };
    },
    'GET /api/v1/users': { body: TENANT_USERS },
    'POST /api/v1/tickets': (call) => {
      const body = call.body as CreateTicketRequest;
      return { status: 201, body: ticketDetails({ id: 'new-ticket', ticketNumber: 'ND-10004', title: body.title, status: 'New' }) };
    },
    'GET /api/v1/tickets/new-ticket': { body: ticketDetails({ id: 'new-ticket', ticketNumber: 'ND-10004', title: 'Brand new' }) },
    'GET /api/v1/tickets/new-ticket/attachments': { body: [] },
  });
}

describe('Ticket queue', () => {
  it('lists tickets with status, priority, assignee and SLA badges, scoped to the tenant', async () => {
    signInAs(USERS.acmeAgent);
    const { find } = mockQueue();
    renderApp('/tickets');

    const row = await screen.findByTestId('ticket-row-ND-10001');
    expect(within(row).getByText('Cannot access billing portal from Chrome')).toBeInTheDocument();
    expect(within(row).getByText('Open')).toBeInTheDocument();
    expect(within(row).getByText('High')).toBeInTheDocument();
    expect(within(row).getByText('You')).toBeInTheDocument();
    expect(within(row).getByText(/Respond: 1h 30m left/)).toBeInTheDocument();
    expect(within(screen.getByTestId('ticket-row-ND-10002')).getByText('Unassigned')).toBeInTheDocument();
    expect(within(screen.getByTestId('ticket-row-ND-10003')).getByText('SLA breached')).toBeInTheDocument();
    await waitFor(() => expect(within(screen.getByTestId('ticket-row-ND-10003')).getByText('Acme Admin')).toBeInTheDocument());

    const [call] = find('GET', '/api/v1/tickets');
    expect(call.url).toContain('page=1&pageSize=50');
    expect(call.headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect(call.headers.get('Authorization')).toBe('Bearer token-Agent');
  });

  it('sends status and priority filters to the API', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockQueue();
    renderApp('/tickets');
    await screen.findByTestId('ticket-row-ND-10001');

    await user.selectOptions(screen.getByLabelText('Filter by status'), 'New');
    await waitFor(() => expect(screen.queryByTestId('ticket-row-ND-10001')).not.toBeInTheDocument());
    expect(screen.getByTestId('ticket-row-ND-10002')).toBeInTheDocument();
    expect(find('GET', '/api/v1/tickets').at(-1)!.url).toContain('status=New');

    await user.selectOptions(screen.getByLabelText('Filter by status'), '');
    await user.selectOptions(screen.getByLabelText('Filter by priority'), 'Urgent');
    await waitFor(() => expect(screen.queryByTestId('ticket-row-ND-10002')).not.toBeInTheDocument());
    expect(screen.getByTestId('ticket-row-ND-10003')).toBeInTheDocument();
    const last = find('GET', '/api/v1/tickets').at(-1)!.url;
    expect(last).toContain('priority=Urgent');
    expect(last).not.toContain('status=');
  });

  it('switches between My Assigned, Unassigned and SLA Breached views', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockQueue();
    renderApp('/tickets');
    await screen.findByTestId('ticket-row-ND-10001');

    await user.click(screen.getByRole('tab', { name: 'My Assigned' }));
    await waitFor(() => expect(screen.queryByTestId('ticket-row-ND-10003')).not.toBeInTheDocument());
    expect(find('GET', '/api/v1/tickets').at(-1)!.url).toContain(`assignedAgentId=${AGENT_ID}`);
    expect(screen.getByTestId('ticket-row-ND-10001')).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Unassigned' }));
    await waitFor(() => expect(screen.getByTestId('ticket-row-ND-10002')).toBeInTheDocument());
    expect(screen.queryByTestId('ticket-row-ND-10001')).not.toBeInTheDocument();
    expect(screen.queryByTestId('ticket-row-ND-10003')).not.toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'SLA Breached' }));
    await waitFor(() => expect(screen.getByTestId('ticket-row-ND-10003')).toBeInTheDocument());
    expect(screen.queryByTestId('ticket-row-ND-10002')).not.toBeInTheDocument();
    expect(screen.getByText(/Showing 1 of 3 tickets/)).toBeInTheDocument();
  });

  it('creates a ticket with the customer pre-selected from GET /api/v1/users', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockQueue();
    renderApp('/tickets');
    await screen.findByTestId('ticket-row-ND-10001');

    await user.click(screen.getByRole('button', { name: 'New ticket' }));
    const dialog = await screen.findByRole('dialog', { name: 'Create ticket' });
    await waitFor(() => expect(within(dialog).getByLabelText('Customer')).toHaveValue(CUSTOMER_ID));

    await user.type(within(dialog).getByLabelText('Title'), 'Brand new');
    await user.type(within(dialog).getByLabelText('Description'), 'Something broke.');
    await user.selectOptions(within(dialog).getByLabelText('Priority'), 'Urgent');
    await user.type(within(dialog).getByLabelText('Tags'), 'Billing, chrome');
    await user.click(within(dialog).getByRole('button', { name: 'Create ticket' }));

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/tickets/new-ticket'));
    expect(find('POST', '/api/v1/tickets')[0].body).toEqual({
      title: 'Brand new',
      description: 'Something broke.',
      priority: 'Urgent',
      channel: 'WebPortal',
      customerId: CUSTOMER_ID,
      tags: ['billing', 'chrome'],
    });
  });

  it('refreshes live on TicketCreated and toasts on SlaBreachAlert', async () => {
    signInAs(USERS.acmeAgent);
    const { find } = mockQueue();
    renderApp('/tickets');
    await screen.findByTestId('ticket-row-ND-10001');
    await waitFor(() => expect(hubConnection('tickets').started).toBe(true));
    const before = find('GET', '/api/v1/tickets').length;

    await emitHub('tickets', 'TicketCreated', {
      ticketId: 't-9',
      tenantId: 'x',
      ticketNumber: 'ND-10009',
      title: 'New',
      status: 'New',
      priority: 'Low',
      customerId: CUSTOMER_ID,
      assignedAgentId: null,
      eventType: 'Created',
      occurredAtUtc: '2026-11-16T18:45:00Z',
    });
    await waitFor(() => expect(find('GET', '/api/v1/tickets').length).toBeGreaterThan(before));

    await emitHub('tickets', 'SlaBreachAlert', {
      ticketId: 't-3',
      ticketNumber: 'ND-10003',
      priority: 'Urgent',
      breachType: 'FirstResponseDueUtc',
      breachedAtUtc: '2026-11-16T19:45:00Z',
    });
    expect(await screen.findByText('SLA breach · ND-10003')).toBeInTheDocument();
    expect(screen.getByText('Urgent priority ticket missed its first-response target.')).toBeInTheDocument();
  });

  it('shows the Live indicator once both hubs connect', async () => {
    signInAs(USERS.acmeAgent);
    mockQueue();
    renderApp('/tickets');
    expect(await screen.findByTestId('realtime-status')).toHaveTextContent('Live');
    expect(hubConnection('tickets').options.url).toMatch(/\/hubs\/tickets$/);
    expect(hubConnection('chat').options.url).toMatch(/\/hubs\/chat$/);
    expect(hubConnection('tickets').options.accessTokenFactory()).toBe('token-Agent');
  });
});
