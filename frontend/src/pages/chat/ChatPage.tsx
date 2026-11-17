import { useQueryClient } from '@tanstack/react-query';
import { ArrowRightLeft, Hand, MessageCirclePlus, MessagesSquare, Send, Ticket, XCircle } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { PageHeader } from '../../components/layout/AppShell';
import { ChatStatusBadge } from '../../components/domain/TicketBadges';
import { Button, Card, EmptyState, ErrorPanel, Input, Skeleton } from '../../components/ui';
import {
  appendChatMessage,
  chatKeys,
  useChatSession,
  useChatSessions,
  useClaimChat,
  useCloseChat,
  useSendChatMessage,
  useStartChat,
} from '../../lib/chat';
import { cn } from '../../lib/cn';
import { formatDateTime, formatRelative } from '../../lib/format';
import { useSessionScope } from '../../lib/queries';
import { useHubEvent, useHubRoom, useTypingSignal } from '../../lib/signalr';
import { useUserNames } from '../../lib/tickets';
import type { ChatMessage, ChatSession, ChatSessionClosed, ChatSessionSummary, ChatTypingIndicatorEvent } from '../../lib/types';
import { useAuthStore } from '../../stores/auth';
import { toast } from '../../stores/toast';

const isOpen = (s: Pick<ChatSessionSummary, 'status'>) => s.status === 'Waiting' || s.status === 'Active';

/**
 * Omnichannel live chat for both audiences:
 * - Customers start sessions and talk to whoever claims them.
 * - Staff work a Waiting / Active queue, claim sessions, reply, and close (optionally to a ticket).
 */
export function ChatPage() {
  const role = useAuthStore((s) => s.user!.role);
  const isStaff = role === 'Agent' || role === 'TenantAdmin';
  const [params, setParams] = useSearchParams();
  const selectedId = params.get('session');
  const select = (id: string | null) =>
    setParams((p) => {
      const next = new URLSearchParams(p);
      if (id) next.set('session', id);
      else next.delete('session');
      return next;
    });

  return (
    <>
      <PageHeader
        title="Live Chat"
        description={
          isStaff
            ? 'Claim waiting conversations and reply in real time over /hubs/chat.'
            : 'Chat with our support team in real time.'
        }
      />
      <div className="grid items-start gap-4 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <SessionList isStaff={isStaff} selectedId={selectedId} onSelect={select} />
        {selectedId ? (
          <ChatThread key={selectedId} sessionId={selectedId} isStaff={isStaff} />
        ) : (
          <Card>
            <EmptyState
              icon={<MessagesSquare className="h-5 w-5" />}
              title={isStaff ? 'Select a conversation' : 'Start or open a conversation'}
              description={
                isStaff ? 'Pick a waiting session from the queue to claim it.' : 'Describe what you need help with and an agent will join shortly.'
              }
            />
          </Card>
        )}
      </div>
    </>
  );
}

function SessionList({ isStaff, selectedId, onSelect }: { isStaff: boolean; selectedId: string | null; onSelect: (id: string) => void }) {
  const { data, isLoading, error, refetch } = useChatSessions();
  const names = useUserNames(isStaff);
  const meId = useAuthStore((s) => s.user!.id);

  const groups = useMemo(() => {
    const sorted = [...(data ?? [])].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc));
    return [
      { title: 'Waiting', items: sorted.filter((s) => s.status === 'Waiting') },
      { title: 'Active', items: sorted.filter((s) => s.status === 'Active') },
      { title: 'Closed', items: sorted.filter((s) => !isOpen(s)) },
    ];
  }, [data]);

  return (
    <Card className="overflow-hidden">
      {!isStaff && <StartChatForm onStarted={onSelect} />}
      {error ? (
        <div className="p-4">
          <ErrorPanel error={error} onRetry={() => void refetch()} />
        </div>
      ) : isLoading ? (
        <div className="space-y-2 p-4">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </div>
      ) : (
        <nav aria-label="Chat sessions" className="max-h-[70vh] overflow-y-auto">
          {groups.map((g) => (
            <section key={g.title} aria-label={`${g.title} sessions`}>
              <h2 className="flex items-center justify-between bg-zinc-50 px-4 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-zinc-500 dark:bg-zinc-900/60">
                {g.title}
                <span className="rounded-full bg-zinc-200 px-1.5 text-[10px] text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400">{g.items.length}</span>
              </h2>
              {g.items.length === 0 ? (
                <p className="px-4 py-3 text-xs text-zinc-400">None</p>
              ) : (
                <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
                  {g.items.map((s) => (
                    <li key={s.id}>
                      <button
                        type="button"
                        data-testid={`chat-session-${s.id}`}
                        aria-current={s.id === selectedId ? 'true' : undefined}
                        onClick={() => onSelect(s.id)}
                        className={cn(
                          'block w-full px-4 py-2.5 text-left transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/40',
                          s.id === selectedId && 'bg-indigo-50/70 dark:bg-indigo-500/10',
                        )}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="truncate text-sm font-medium">{s.subject}</span>
                          <ChatStatusBadge status={s.status} />
                        </div>
                        <p className="mt-0.5 truncate text-[11px] text-zinc-500">
                          {isStaff && `${names.get(s.customerId)?.fullName ?? 'Customer'} · `}
                          {s.assignedAgentId
                            ? s.assignedAgentId === meId
                              ? 'with you · '
                              : `with ${names.get(s.assignedAgentId)?.fullName ?? 'an agent'} · `
                            : ''}
                          {formatRelative(s.createdAtUtc)}
                          {typeof s.messageCount === 'number' && ` · ${s.messageCount} msg`}
                        </p>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          ))}
        </nav>
      )}
    </Card>
  );
}

function StartChatForm({ onStarted }: { onStarted: (id: string) => void }) {
  const [subject, setSubject] = useState('');
  const start = useStartChat();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const s = subject.trim();
    if (!s) return;
    start.mutate(s, {
      onSuccess: (session) => {
        setSubject('');
        toast.success('Chat started', 'An agent will join shortly.');
        onStarted(session.id);
      },
    });
  };

  return (
    <form onSubmit={submit} className="space-y-2 border-b border-zinc-100 p-4 dark:border-zinc-800">
      <label htmlFor="chat-subject" className="block text-xs font-medium text-zinc-700 dark:text-zinc-300">
        Start a new chat
      </label>
      <Input id="chat-subject" value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="What can we help with?" />
      <Button type="submit" size="sm" className="w-full" icon={<MessageCirclePlus className="h-4 w-4" />} loading={start.isPending} disabled={!subject.trim()}>
        Start chat
      </Button>
    </form>
  );
}

const TYPING_TTL_MS = 6_000;

function ChatThread({ sessionId, isStaff }: { sessionId: string; isStaff: boolean }) {
  const me = useAuthStore((s) => s.user)!;
  const scope = useSessionScope();
  const qc = useQueryClient();
  const { data: session, isLoading, error, refetch } = useChatSession(sessionId);
  const names = useUserNames(isStaff);
  const claim = useClaimChat();
  const close = useCloseChat();
  const send = useSendChatMessage(sessionId);
  const [draft, setDraft] = useState('');
  const [typing, setTyping] = useState<Record<string, string>>({});
  const typingTimers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const endRef = useRef<HTMLDivElement>(null);
  const key = chatKeys.session(sessionId, scope);

  useHubRoom('chat', 'JoinChatSession', 'LeaveChatSession', sessionId);
  const typingSignal = useTypingSignal('chat', 'SendTypingIndicator', sessionId);

  useHubEvent<ChatMessage>('chat', 'ChatMessageReceived', (m) => {
    if (m.chatSessionId !== sessionId) return;
    appendChatMessage(qc, key, m);
    clearTyping(m.senderUserId);
    void qc.invalidateQueries({ queryKey: chatKeys.sessionsRoot });
  });
  useHubEvent<ChatSession>('chat', 'ChatSessionClaimed', (s) => {
    if (s.id !== sessionId) return;
    qc.setQueryData<ChatSession>(key, (prev) => (prev ? { ...prev, ...s, messages: prev.messages } : prev));
    if (!isStaff) toast.info('An agent joined the chat');
  });
  useHubEvent<ChatSessionClosed>('chat', 'ChatSessionClosed', (s) => {
    if (s.id !== sessionId) return;
    qc.setQueryData<ChatSession>(key, (prev) =>
      prev ? { ...prev, status: s.status, convertedTicketId: s.convertedTicketId, closedAtUtc: s.closedAtUtc } : prev,
    );
  });
  useHubEvent<ChatTypingIndicatorEvent>('chat', 'ChatTypingIndicator', (e) => {
    if (e.sessionId !== sessionId || e.userId === me.id) return;
    if (!e.isTyping) return clearTyping(e.userId);
    setTyping((t) => ({ ...t, [e.userId]: e.userName || 'Someone' }));
    const timers = typingTimers.current;
    clearTimeout(timers.get(e.userId));
    timers.set(e.userId, setTimeout(() => clearTyping(e.userId), TYPING_TTL_MS));
  });

  function clearTyping(userId: string) {
    clearTimeout(typingTimers.current.get(userId));
    typingTimers.current.delete(userId);
    setTyping((t) => {
      if (!(userId in t)) return t;
      const next = { ...t };
      delete next[userId];
      return next;
    });
  }

  useEffect(() => {
    const timers = typingTimers.current;
    return () => timers.forEach((t) => clearTimeout(t));
  }, []);

  const messages = useMemo(
    () => [...(session?.messages ?? [])].sort((a, b) => a.sentAtUtc.localeCompare(b.sentAtUtc)),
    [session?.messages],
  );

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [messages.length]);

  if (error) return <ErrorPanel error={error} onRetry={() => void refetch()} />;
  if (isLoading || !session) return <Skeleton className="h-[28rem] w-full" />;

  const open = isOpen(session);
  const needsClaim = isStaff && session.status === 'Waiting';
  const canSend = open && !needsClaim;
  const typingNames = Object.values(typing);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const content = draft.trim();
    if (!content || !canSend) return;
    typingSignal.stop();
    send.mutate(content, { onSuccess: () => setDraft('') });
  };

  const doClose = (convertToTicket: boolean) =>
    close.mutate(
      { id: session.id, convertToTicket },
      {
        onSuccess: (r) =>
          toast.success(r.convertedTicketId ? 'Chat converted to a ticket' : 'Chat closed', r.convertedTicketId ? 'The transcript is now on the ticket.' : undefined),
      },
    );

  return (
    <Card className="flex min-h-[28rem] flex-col overflow-hidden">
      <div className="flex flex-wrap items-center gap-3 border-b border-zinc-100 px-5 py-3 dark:border-zinc-800">
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-sm font-semibold">{session.subject}</h2>
          <p className="text-[11px] text-zinc-500">
            Started {formatDateTime(session.createdAtUtc)}
            {isStaff && ` · ${names.get(session.customerId)?.fullName ?? 'Customer'}`}
            {session.assignedAgentId &&
              ` · agent: ${session.assignedAgentId === me.id ? 'you' : (names.get(session.assignedAgentId)?.fullName ?? 'assigned')}`}
          </p>
        </div>
        <ChatStatusBadge status={session.status} />
        {isStaff && session.status === 'Waiting' && (
          <Button size="sm" icon={<Hand className="h-3.5 w-3.5" />} loading={claim.isPending} onClick={() => claim.mutate(session.id)}>
            Claim Session
          </Button>
        )}
        {isStaff && session.status === 'Active' && (
          <>
            <Button size="sm" variant="secondary" icon={<XCircle className="h-3.5 w-3.5" />} disabled={close.isPending} onClick={() => doClose(false)}>
              Close Session
            </Button>
            <Button size="sm" icon={<ArrowRightLeft className="h-3.5 w-3.5" />} loading={close.isPending} onClick={() => doClose(true)}>
              Close & Convert to Ticket
            </Button>
          </>
        )}
        {!isStaff && open && (
          <Button size="sm" variant="secondary" icon={<XCircle className="h-3.5 w-3.5" />} loading={close.isPending} onClick={() => doClose(false)}>
            End chat
          </Button>
        )}
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto bg-zinc-50/50 px-5 py-4 dark:bg-zinc-950/30" aria-label="Messages" role="log">
        {messages.length === 0 && (
          <p className="py-10 text-center text-sm text-zinc-500">
            {session.status === 'Waiting' ? 'Waiting for an agent to join…' : 'No messages yet.'}
          </p>
        )}
        {messages.map((m) => {
          const mine = m.senderUserId === me.id;
          const fromCustomer = m.senderRole === 'Customer';
          return (
            <div key={m.id} data-testid={`chat-message-${m.id}`} className={cn('flex', mine ? 'justify-end' : 'justify-start')}>
              <div
                className={cn(
                  'max-w-[80%] rounded-2xl px-3.5 py-2 text-sm shadow-sm',
                  mine
                    ? 'rounded-br-sm bg-indigo-600 text-white'
                    : 'rounded-bl-sm border border-zinc-200 bg-white text-zinc-800 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-200',
                )}
              >
                {!mine && (
                  <p className="mb-0.5 text-[11px] font-semibold opacity-70">
                    {names.get(m.senderUserId)?.fullName ?? (fromCustomer ? 'Customer' : 'Support agent')} · {m.senderRole}
                  </p>
                )}
                <p className="whitespace-pre-wrap">{m.content}</p>
                <p className={cn('mt-0.5 text-right text-[10px]', mine ? 'text-indigo-200' : 'text-zinc-400')}>{formatDateTime(m.sentAtUtc)}</p>
              </div>
            </div>
          );
        })}
        <div ref={endRef} />
      </div>

      <div className="border-t border-zinc-100 px-5 py-3 dark:border-zinc-800">
        <p className="mb-1.5 h-4 text-[11px] text-zinc-500" aria-live="polite" data-testid="chat-typing">
          {typingNames.length > 0 && `${typingNames.join(', ')} ${typingNames.length === 1 ? 'is' : 'are'} typing…`}
        </p>
        {session.convertedTicketId ? (
          <p className="flex items-center gap-2 text-sm text-violet-700 dark:text-violet-300">
            <Ticket className="h-4 w-4" aria-hidden />
            Converted to a ticket.
            {isStaff && (
              <Link to={`/tickets/${session.convertedTicketId}`} className="font-medium underline">
                Open ticket
              </Link>
            )}
          </p>
        ) : !open ? (
          <p className="text-sm text-zinc-500">This conversation is closed.</p>
        ) : needsClaim ? (
          <p className="text-sm text-amber-700 dark:text-amber-300">Claim this session to start replying.</p>
        ) : (
          <form onSubmit={submit} className="flex gap-2">
            <Input
              aria-label="Message"
              value={draft}
              onChange={(e) => {
                setDraft(e.target.value);
                typingSignal.ping(Boolean(e.target.value.trim()));
              }}
              onBlur={typingSignal.stop}
              placeholder="Type a message…"
              autoComplete="off"
            />
            <Button type="submit" icon={<Send className="h-4 w-4" />} loading={send.isPending} disabled={!draft.trim()}>
              Send
            </Button>
          </form>
        )}
      </div>
    </Card>
  );
}
