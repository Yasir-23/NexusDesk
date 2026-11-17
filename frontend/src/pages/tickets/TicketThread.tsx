import { Lock, MessageSquare, Send } from 'lucide-react';
import { forwardRef, type FormEvent, type KeyboardEvent } from 'react';
import { Badge, Button, Card, Textarea } from '../../components/ui';
import { cn } from '../../lib/cn';
import { formatDateTime } from '../../lib/format';
import { useTypingSignal } from '../../lib/signalr';
import { useAddComment } from '../../lib/tickets';
import type { CommentType, TenantUser, TicketComment } from '../../lib/types';
import { useAuthStore } from '../../stores/auth';

export function CommentList({ comments, users }: { comments: TicketComment[]; users: Map<string, TenantUser> }) {
  const me = useAuthStore((s) => s.user)!;
  const sorted = [...comments].sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc));

  if (sorted.length === 0) {
    return <p className="py-6 text-center text-sm text-zinc-500">No replies yet. Be the first to respond.</p>;
  }

  return (
    <ol className="space-y-3" aria-label="Conversation">
      {sorted.map((c) => {
        const internal = c.type === 'InternalNote';
        const author = c.authorUserId === me.id ? 'You' : (users.get(c.authorUserId)?.fullName ?? (c.isCustomer ? 'Customer' : 'Agent'));
        return (
          <li
            key={c.id}
            data-testid={`comment-${c.id}`}
            data-comment-type={c.type}
            className={cn(
              'rounded-xl border p-3.5',
              internal
                ? 'border-amber-300 bg-amber-50 dark:border-amber-500/40 dark:bg-amber-500/10'
                : c.isCustomer
                  ? 'border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900'
                  : 'border-indigo-200 bg-indigo-50/50 dark:border-indigo-500/30 dark:bg-indigo-500/5',
            )}
          >
            <div className="mb-1.5 flex flex-wrap items-center gap-2 text-xs">
              <span className="font-semibold text-zinc-900 dark:text-zinc-100">{author}</span>
              {c.isCustomer ? <Badge tone="neutral">Customer</Badge> : <Badge tone="indigo">Staff</Badge>}
              {internal && (
                <Badge tone="amber" icon={<Lock className="h-3 w-3" aria-hidden />}>
                  Internal note
                </Badge>
              )}
              <span className="ml-auto text-zinc-500">{formatDateTime(c.createdAtUtc)}</span>
            </div>
            <p className="whitespace-pre-wrap text-sm text-zinc-800 dark:text-zinc-200">{c.body}</p>
          </li>
        );
      })}
    </ol>
  );
}

interface ComposerProps {
  ticketId: string;
  draft: string;
  onDraftChange: (value: string) => void;
  type: CommentType;
  onTypeChange: (type: CommentType) => void;
}

/** `POST /api/v1/tickets/{id}/comments` with a PublicReply / InternalNote toggle and typing presence. */
export const Composer = forwardRef<HTMLTextAreaElement, ComposerProps>(function Composer(
  { ticketId, draft, onDraftChange, type, onTypeChange },
  ref,
) {
  const add = useAddComment(ticketId);
  // SetTypingState(ticketId, true/false), cleared on idle, send, blur and unmount.
  const { ping, stop: stopTyping } = useTypingSignal('tickets', 'SetTypingState', ticketId);

  const onChange = (value: string) => {
    onDraftChange(value);
    ping(Boolean(value.trim()));
  };

  const submit = (e?: FormEvent) => {
    e?.preventDefault();
    const body = draft.trim();
    if (!body || add.isPending) return;
    stopTyping();
    add.mutate({ body, type }, { onSuccess: () => onDraftChange('') });
  };

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) submit();
  };

  const internal = type === 'InternalNote';

  return (
    <Card className={cn('overflow-hidden', internal && 'border-amber-300 dark:border-amber-500/40')}>
      <form onSubmit={submit}>
        <div role="radiogroup" aria-label="Reply type" className="flex border-b border-zinc-100 dark:border-zinc-800">
          {(
            [
              { id: 'PublicReply', label: 'Public reply', icon: MessageSquare },
              { id: 'InternalNote', label: 'Internal note', icon: Lock },
            ] as const
          ).map((opt) => (
            <button
              key={opt.id}
              type="button"
              role="radio"
              aria-checked={type === opt.id}
              onClick={() => onTypeChange(opt.id)}
              className={cn(
                'flex items-center gap-1.5 border-b-2 px-4 py-2 text-xs font-medium transition-colors',
                type === opt.id
                  ? opt.id === 'InternalNote'
                    ? 'border-amber-500 text-amber-700 dark:text-amber-300'
                    : 'border-indigo-500 text-indigo-700 dark:text-indigo-300'
                  : 'border-transparent text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200',
              )}
            >
              <opt.icon className="h-3.5 w-3.5" aria-hidden />
              {opt.label}
            </button>
          ))}
        </div>
        <div className={cn('p-3', internal && 'bg-amber-50/60 dark:bg-amber-500/5')}>
          <Textarea
            ref={ref}
            aria-label="Reply"
            rows={4}
            value={draft}
            onChange={(e) => onChange(e.target.value)}
            onBlur={stopTyping}
            onKeyDown={onKeyDown}
            placeholder={internal ? 'Private note for staff only — never shown to the customer…' : 'Write a reply to the customer…'}
            className={cn('border-0 shadow-none focus:ring-0', internal && 'bg-transparent dark:bg-transparent')}
          />
          <div className="mt-2 flex items-center justify-between gap-2">
            <p className="text-[11px] text-zinc-500">
              {internal ? 'Visible to agents and admins only.' : 'The customer will see this reply.'} Ctrl+Enter to send.
            </p>
            <Button
              type="submit"
              size="sm"
              variant={internal ? 'secondary' : 'primary'}
              icon={<Send className="h-3.5 w-3.5" />}
              loading={add.isPending}
              disabled={!draft.trim()}
              className={cn(internal && 'border-amber-300 text-amber-800 dark:border-amber-500/40 dark:text-amber-200')}
            >
              {internal ? 'Add internal note' : 'Send reply'}
            </Button>
          </div>
        </div>
      </form>
    </Card>
  );
});
