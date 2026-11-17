import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { ChatSession } from '../../lib/types';
import { emitHub, hubConnection } from '../../test/fakeHub';
import { AGENT_ID, CHAT_SESSIONS, CUSTOMER_ID, TENANT_USERS, chatMessage } from '../../test/fixtures';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

/** Stateful chat backend: claim / message / close mutate the sessions. */
function mockChat() {
  const sessions: ChatSession[] = CHAT_SESSIONS.map((s) => ({ ...s, messages: [...(s.messages ?? [])] }));
  const byId = (id: string) => sessions.find((s) => s.id === id)!;
  let n = 0;
  const routes: Parameters<typeof mockFetch>[0] = {
    'GET /api/v1/chat/sessions': () => ({ body: sessions.map(({ messages, ...s }) => ({ ...s, messageCount: messages?.length ?? 0 })) }),
    'POST /api/v1/chat/sessions': (call) => {
      const created: ChatSession = {
        ...CHAT_SESSIONS[0],
        id: 'chat-new',
        subject: (call.body as { subject: string }).subject,
        messages: [],
      };
      sessions.push(created);
      return { status: 201, body: created };
    },
    'GET /api/v1/users': { body: TENANT_USERS },
  };
  for (const s of sessions) {
    routes[`GET /api/v1/chat/sessions/${s.id}`] = () => ({ body: byId(s.id) });
    routes[`POST /api/v1/chat/sessions/${s.id}/claim`] = () => {
      Object.assign(byId(s.id), { status: 'Active', assignedAgentId: AGENT_ID });
      return { body: byId(s.id) };
    };
    routes[`POST /api/v1/chat/sessions/${s.id}/messages`] = (call) => {
      const m = chatMessage({
        id: `sent-${++n}`,
        chatSessionId: s.id,
        senderUserId: AGENT_ID,
        senderRole: 'Agent',
        content: (call.body as { content: string }).content,
      });
      byId(s.id).messages!.push(m);
      return { status: 201, body: m };
    };
    routes[`POST /api/v1/chat/sessions/${s.id}/close`] = (call) => {
      const { convertToTicket } = call.body as { convertToTicket: boolean };
      const closed = {
        id: s.id,
        tenantId: s.tenantId,
        status: convertToTicket ? 'ConvertedToTicket' : 'Closed',
        convertedTicketId: convertToTicket ? 'ticket-from-chat' : null,
        closedAtUtc: '2026-11-16T19:20:00Z',
      } as const;
      Object.assign(byId(s.id), closed);
      return { body: closed };
    };
  }
  routes['GET /api/v1/chat/sessions/chat-new'] = () => ({ body: byId('chat-new') });
  return mockFetch(routes);
}

describe('Live chat — agent console', () => {
  it('groups the queue into Waiting / Active / Closed', async () => {
    signInAs(USERS.acmeAgent);
    mockChat();
    renderApp('/chat');

    const waiting = await screen.findByRole('region', { name: 'Waiting sessions' });
    expect(within(waiting).getByText('Pre-sales inquiry regarding Enterprise dedicated database')).toBeInTheDocument();
    const active = screen.getByRole('region', { name: 'Active sessions' });
    expect(within(active).getByText('Invoice question')).toBeInTheDocument();
    await waitFor(() => expect(within(active).getByText(/with you/)).toBeInTheDocument());
    expect(within(screen.getByRole('region', { name: 'Closed sessions' })).getByText('None')).toBeInTheDocument();
  });

  it('claims a waiting session, joins its room, exchanges messages and converts it to a ticket', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockChat();
    renderApp('/chat');

    await user.click(await screen.findByTestId('chat-session-chat-waiting'));
    expect(await screen.findByText('Claim this session to start replying.')).toBeInTheDocument();
    await waitFor(() => expect(hubConnection('chat').calls('JoinChatSession')).toContainEqual(['chat-waiting']));

    await user.click(screen.getByRole('button', { name: 'Claim Session' }));
    await waitFor(() => expect(find('POST', '/api/v1/chat/sessions/chat-waiting/claim')).toHaveLength(1));
    const input = await screen.findByLabelText('Message');

    // Customer message + typing arrive over the hub.
    await emitHub('chat', 'ChatTypingIndicator', { sessionId: 'chat-waiting', userId: CUSTOMER_ID, userName: 'Acme Customer', isTyping: true });
    expect(screen.getByTestId('chat-typing')).toHaveTextContent('Acme Customer is typing…');
    await emitHub('chat', 'ChatMessageReceived', chatMessage());
    expect(await screen.findByTestId('chat-message-m-1')).toHaveTextContent('Hi, does Enterprise include a dedicated database?');
    expect(screen.getByTestId('chat-typing')).toHaveTextContent('');

    await user.type(input, 'Yes — Enterprise runs on its own database.');
    expect(hubConnection('chat').calls('SendTypingIndicator')).toContainEqual(['chat-waiting', true]);
    await user.click(screen.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(find('POST', '/api/v1/chat/sessions/chat-waiting/messages')).toHaveLength(1));
    expect(find('POST', '/api/v1/chat/sessions/chat-waiting/messages')[0].body).toEqual({
      content: 'Yes — Enterprise runs on its own database.',
    });
    expect(await screen.findByTestId('chat-message-sent-1')).toBeInTheDocument();
    expect(hubConnection('chat').calls('SendTypingIndicator')).toContainEqual(['chat-waiting', false]);

    await user.click(screen.getByRole('button', { name: 'Close & Convert to Ticket' }));
    await waitFor(() => expect(find('POST', '/api/v1/chat/sessions/chat-waiting/close')).toHaveLength(1));
    expect(find('POST', '/api/v1/chat/sessions/chat-waiting/close')[0].body).toEqual({ convertToTicket: true });
    expect(await screen.findByRole('link', { name: 'Open ticket' })).toHaveAttribute('href', '/tickets/ticket-from-chat');
  });

  it('closes an active session without conversion', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockChat();
    renderApp('/chat?session=chat-active');

    await user.click(await screen.findByRole('button', { name: 'Close Session' }));
    await waitFor(() => expect(find('POST', '/api/v1/chat/sessions/chat-active/close')).toHaveLength(1));
    expect(find('POST', '/api/v1/chat/sessions/chat-active/close')[0].body).toEqual({ convertToTicket: false });
    expect(await screen.findByText('This conversation is closed.')).toBeInTheDocument();
  });
});

describe('Live chat — customer view', () => {
  it('starts a session and sees the agent claim it live', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    const { find } = mockChat();
    renderApp('/portal/chat'); // legacy portal path redirects to the shared console

    await user.type(await screen.findByLabelText('Start a new chat'), 'Need SSO help');
    await user.click(screen.getByRole('button', { name: 'Start chat' }));

    await waitFor(() => expect(find('POST', '/api/v1/chat/sessions')).toHaveLength(1));
    expect(find('POST', '/api/v1/chat/sessions')[0].body).toEqual({ subject: 'Need SSO help' });
    expect(await screen.findByText('Waiting for an agent to join…')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Claim Session' })).not.toBeInTheDocument();
    expect(find('GET', '/api/v1/users')).toHaveLength(0); // staff-only endpoint is never called

    await emitHub('chat', 'ChatSessionClaimed', { ...CHAT_SESSIONS[0], id: 'chat-new', status: 'Active', assignedAgentId: AGENT_ID });
    expect(await screen.findByText('An agent joined the chat')).toBeInTheDocument();

    await emitHub('chat', 'ChatMessageReceived', chatMessage({ id: 'agent-1', chatSessionId: 'chat-new', senderUserId: AGENT_ID, senderRole: 'Agent', content: 'Hello! How may I assist you today?' }));
    const msg = await screen.findByTestId('chat-message-agent-1');
    expect(msg).toHaveTextContent('Support agent · Agent');
    expect(screen.getByRole('button', { name: 'End chat' })).toBeInTheDocument();
  });
});
