import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { useAuthStore } from '../../stores/auth';
import type { PortalCreateTicketRequest } from '../../lib/types';
import { CUSTOMER_ID, KB_SEARCH, paged, ticketDetails, ticketSummary } from '../../test/fixtures';
import { notification, notificationsPage } from '../../test/fixtures.m3';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

const MY_TICKETS = [
  ticketSummary({ assignedAgentId: null }),
  ticketSummary({ id: 't-2', ticketNumber: 'ND-10002', title: 'Invoice PDF is blank', status: 'Resolved', priority: 'Low' }),
];

/** Server-scrubbed portal copy: only public replies, no activity log. */
const PORTAL_TICKET = ticketDetails({
  comments: [ticketDetails().comments[0]],
  activityLogs: [],
});

describe('Customer portal', () => {
  it('lists my requests and filters by status', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    const { find } = mockFetch({
      'GET /api/v1/portal/tickets': (call) => {
        const status = new URL(call.url, 'http://l').searchParams.get('status');
        return { body: paged(MY_TICKETS.filter((t) => !status || t.status === status)) };
      },
    });
    renderApp('/portal');

    expect(await screen.findByTestId('portal-row-ND-10001')).toHaveTextContent('Cannot access billing portal from Chrome');
    expect(screen.getByTestId('portal-row-ND-10002')).toHaveTextContent('Resolved');
    const list = find('GET', '/api/v1/portal/tickets')[0];
    expect(list.headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect(list.url).toContain('page=1&pageSize=20');

    await user.selectOptions(screen.getByLabelText('Filter by status'), 'Resolved');
    await waitFor(() => expect(screen.queryByTestId('portal-row-ND-10001')).not.toBeInTheDocument());
    expect(find('GET', '/api/v1/portal/tickets').at(-1)!.url).toContain('status=Resolved');
  });

  it('suggests KB articles while typing the title, then submits the request', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    const created = ticketDetails({ id: 'new-ticket', ticketNumber: 'ND-10077', title: 'Cannot login with SSO', comments: [], activityLogs: [] });
    const { find } = mockFetch({
      'GET /api/v1/portal/tickets': { body: paged([]) },
      'GET /api/v1/kb/search': { body: KB_SEARCH },
      'POST /api/v1/portal/tickets': { status: 201, body: created },
      'GET /api/v1/portal/tickets/new-ticket': { body: created },
    });
    renderApp('/portal');

    await user.click(await screen.findByRole('button', { name: 'Submit New Request' }));
    const dialog = await screen.findByRole('dialog', { name: 'Submit a new request' });
    await user.type(within(dialog).getByLabelText('Subject'), 'Cannot login with SSO');

    const suggestions = await within(dialog).findByRole('region', { name: 'Suggested articles' });
    expect(await within(suggestions).findByText('Troubleshooting SSO Login Failures')).toBeInTheDocument();
    expect(within(suggestions).getByRole('link', { name: /Troubleshooting SSO/ })).toHaveAttribute('href', '/kb?article=troubleshooting-sso-login-failures');
    const search = find('GET', '/api/v1/kb/search').at(-1)!;
    expect(new URL(search.url, 'http://l').searchParams.get('q')).toBe('Cannot login with SSO');
    expect(search.url).toContain('limit=3');

    await user.type(within(dialog).getByLabelText('Description'), 'The Okta button loops back to the login page.');
    await user.selectOptions(within(dialog).getByLabelText('Priority'), 'High');
    await user.type(within(dialog).getByLabelText('Tags (optional)'), 'SSO, okta');
    await user.click(within(dialog).getByRole('button', { name: 'Submit request' }));

    await waitFor(() => expect(find('POST', '/api/v1/portal/tickets')).toHaveLength(1));
    expect(find('POST', '/api/v1/portal/tickets')[0].body).toEqual<PortalCreateTicketRequest>({
      title: 'Cannot login with SSO',
      description: 'The Okta button loops back to the login page.',
      priority: 'High',
      tags: ['sso', 'okta'],
    });
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/portal/tickets/new-ticket'));
    expect(await screen.findByRole('heading', { name: 'Cannot login with SSO' })).toBeInTheDocument();
  });

  it('renders the scrubbed customer ticket (no internal notes) and posts a reply', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    let ticket = PORTAL_TICKET;
    const { find } = mockFetch({
      [`GET /api/v1/portal/tickets/${PORTAL_TICKET.id}`]: () => ({ body: ticket }),
      [`POST /api/v1/portal/tickets/${PORTAL_TICKET.id}/replies`]: (call) => {
        const comment = {
          id: 'c-new',
          authorUserId: CUSTOMER_ID,
          isCustomer: true,
          type: 'PublicReply' as const,
          body: (call.body as { message: string }).message,
          createdAtUtc: '2026-11-16T19:30:00Z',
        };
        ticket = { ...ticket, comments: [...ticket.comments, comment] };
        return { status: 201, body: comment };
      },
    });
    renderApp(`/portal/tickets/${PORTAL_TICKET.id}`);

    expect(await screen.findByRole('heading', { name: PORTAL_TICKET.title })).toBeInTheDocument();
    expect(screen.getByTestId('portal-comment-c-1')).toHaveTextContent('It started after the MFA rollout.');
    expect(screen.queryByText(/stale session cookie/)).not.toBeInTheDocument();
    expect(screen.queryByText('Internal note')).not.toBeInTheDocument();
    expect(screen.getByTestId('scrub-notice')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Your reply'), 'Thank you, I found the file!');
    await user.click(screen.getByRole('button', { name: 'Send reply' }));

    await waitFor(() => expect(find('POST', '/replies')).toHaveLength(1));
    expect(find('POST', '/replies')[0].body).toEqual({ message: 'Thank you, I found the file!' });
    expect(await screen.findByTestId('portal-comment-c-new')).toHaveTextContent('You');
    expect(screen.getByLabelText('Your reply')).toHaveValue('');
  });

  it("shows a not-found state for another customer's ticket (404)", async () => {
    signInAs(USERS.acmeCustomer);
    mockFetch({});
    renderApp('/portal/tickets/someone-elses');
    expect(await screen.findByText('Request not found')).toBeInTheDocument();
  });
});

describe('Notifications bell', () => {
  it('shows the unread badge, marks one read, then marks all read', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    let items = [
      notification(),
      notification({ id: 'n-2', title: 'Request resolved', linkUrl: null }),
      notification({ id: 'n-3', title: 'Old news', isRead: true, readAtUtc: '2026-11-15T00:00:00Z' }),
    ];
    const { find } = mockFetch({
      'GET /api/v1/portal/tickets': { body: paged([]) },
      'GET /api/v1/notifications': () => ({ body: notificationsPage(items) }),
      'POST /api/v1/notifications/n-2/read': () => {
        items = items.map((n) => (n.id === 'n-2' ? { ...n, isRead: true } : n));
        return { body: items[1] };
      },
      'POST /api/v1/notifications/read-all': () => {
        const markedCount = items.filter((n) => !n.isRead).length;
        items = items.map((n) => ({ ...n, isRead: true }));
        return { body: { markedCount } };
      },
    });
    renderApp('/portal');

    expect(await screen.findByTestId('notification-badge')).toHaveTextContent('2');
    const get = find('GET', '/api/v1/notifications')[0];
    expect(get.url).toContain('unreadOnly=false&page=1&pageSize=10');

    await user.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }));
    await user.click(screen.getByTestId('notification-n-2'));
    await waitFor(() => expect(find('POST', '/api/v1/notifications/n-2/read')).toHaveLength(1));
    await waitFor(() => expect(screen.getByTestId('notification-badge')).toHaveTextContent('1'));

    await user.click(screen.getByRole('button', { name: 'Mark all read' }));
    await waitFor(() => expect(find('POST', '/api/v1/notifications/read-all')).toHaveLength(1));
    await waitFor(() => expect(screen.queryByTestId('notification-badge')).not.toBeInTheDocument());
    expect(screen.getByTestId('notification-n-1')).toHaveAttribute('data-read', 'true');
  });

  it("routes a customer's ticket link to the portal detail page", async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    mockFetch({
      'GET /api/v1/portal/tickets': { body: paged([]) },
      'GET /api/v1/notifications': { body: notificationsPage([notification()]) },
      'POST /api/v1/notifications/n-1/read': { body: notification({ isRead: true }) },
    });
    renderApp('/portal');
    await user.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    await user.click(screen.getByTestId('notification-n-1'));
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/portal/tickets/652f5d1c-aed3-426d-aad5-48273c5140ad'));
  });

  it('is hidden for SuperAdmin (no tenant inbox)', async () => {
    signInAs(USERS.superAdmin);
    const { find } = mockFetch({ 'GET /api/v1/platform/tenants': { body: { page: 1, pageSize: 20, totalCount: 0, items: [] } } });
    renderApp('/account');
    await screen.findByRole('heading', { name: 'My Profile' });
    expect(screen.queryByRole('button', { name: /^Notifications/ })).not.toBeInTheDocument();
    expect(find('GET', '/api/v1/notifications')).toHaveLength(0);
  });
});

describe('GDPR right to erasure', () => {
  const ME = {
    id: CUSTOMER_ID,
    email: 'customer@acme.com',
    fullName: 'Acme Customer',
    phoneNumber: null,
    role: 'Customer',
    tenantId: 't',
    isActive: true,
    permissions: [],
    ssoProvider: null,
    lastLoginAtUtc: null,
    createdAtUtc: '2026-11-10T00:00:00Z',
  };

  it('requires typed confirmation, calls forget-me, shows anonymizedAtUtc and signs out', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    const { find } = mockFetch({
      'GET /api/v1/auth/me': { body: ME },
      'POST /api/v1/privacy/forget-me': { body: { anonymized: true, userId: CUSTOMER_ID, anonymizedAtUtc: '2026-11-16T18:30:00Z' } },
    });
    renderApp('/account');

    const zone = await screen.findByTestId('gdpr-danger-zone');
    expect(within(zone).getByText('Privacy & GDPR Data Erasure')).toBeInTheDocument();
    await user.click(within(zone).getByRole('button', { name: 'Erase my data' }));

    const dialog = await screen.findByRole('dialog', { name: 'Erase your personal data?' });
    const confirm = within(dialog).getByRole('button', { name: 'Permanently erase' });
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText('Type ERASE to confirm'), 'ERASE');
    await user.click(confirm);

    const done = await screen.findByTestId('erasure-confirmation');
    expect(within(done).getByTestId('anonymized-at')).toHaveTextContent(/Nov 16/);
    const post = find('POST', '/api/v1/privacy/forget-me')[0];
    expect(post.headers.get('Authorization')).toBe('Bearer token-Customer');
    expect(post.headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect(useAuthStore.getState().accessToken).not.toBeNull(); // still signed in until acknowledged

    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/login'));
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(await screen.findByText('Your personal data has been erased')).toBeInTheDocument();
  });

  it('is not offered to SuperAdmin', async () => {
    signInAs(USERS.superAdmin);
    mockFetch({ 'GET /api/v1/auth/me': { body: { ...ME, role: 'SuperAdmin', email: 'superadmin@nexusdesk.com' } } });
    renderApp('/account');
    await screen.findByText('superadmin@nexusdesk.com');
    expect(screen.queryByTestId('gdpr-danger-zone')).not.toBeInTheDocument();
  });
});
