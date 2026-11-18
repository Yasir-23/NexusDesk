import { BookOpen, Lightbulb } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Dialog, ErrorPanel, Field, Input, Select, Spinner, Textarea } from '../../components/ui';
import { useKbSearch } from '../../lib/kb';
import { useCreatePortalTicket } from '../../lib/portal';
import { TICKET_PRIORITIES, type TicketPriority } from '../../lib/types';
import { toast } from '../../stores/toast';

function useDebounced<T>(value: T, ms = 350): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** `POST /api/v1/portal/tickets` with live KB deflection (`GET /api/v1/kb/search`) on the title. */
export function SubmitRequestDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate();
  const create = useCreatePortalTicket();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<TicketPriority>('Medium');
  const [tags, setTags] = useState('');

  const term = useDebounced(open ? title : '');
  const suggestions = useKbSearch(term, 3);
  const showSuggestions = term.trim().length >= 3;

  const close = () => {
    setTitle('');
    setDescription('');
    setPriority('Medium');
    setTags('');
    create.reset();
    onClose();
  };

  const canSubmit = title.trim().length > 0 && description.trim().length > 0;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    try {
      const ticket = await create.mutateAsync({
        title: title.trim(),
        description: description.trim(),
        priority,
        tags: tags
          .split(',')
          .map((t) => t.trim().toLowerCase())
          .filter(Boolean),
      });
      toast.success(`Request ${ticket.ticketNumber} submitted`, 'Our team will get back to you soon.');
      close();
      navigate(`/portal/tickets/${ticket.id}`);
    } catch {
      /* inline */
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      size="lg"
      title="Submit a new request"
      description="Describe the problem. We'll suggest help articles as you type — one of them may solve it instantly."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form="portal-request-form" loading={create.isPending} disabled={!canSubmit}>
            Submit request
          </Button>
        </>
      }
    >
      <form id="portal-request-form" onSubmit={submit} className="space-y-4">
        {create.error && <ErrorPanel error={create.error} />}
        <Field label="Subject">
          {(id) => <Input id={id} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="I can't log in with SSO" required />}
        </Field>
        {showSuggestions && (
          <section
            aria-label="Suggested articles"
            data-testid="deflection-suggestions"
            className="rounded-lg border border-indigo-200 bg-indigo-50/60 p-3 dark:border-indigo-500/30 dark:bg-indigo-500/5"
          >
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold text-indigo-800 dark:text-indigo-200">
              <Lightbulb className="h-3.5 w-3.5" aria-hidden /> These articles might answer your question
              {suggestions.isFetching && <Spinner className="h-3 w-3" />}
            </p>
            {suggestions.data?.length ? (
              <ul className="space-y-1.5">
                {suggestions.data.map((a) => (
                  <li key={a.id}>
                    <a
                      href={`/kb?article=${encodeURIComponent(a.slug)}`}
                      target="_blank"
                      rel="noreferrer"
                      className="flex items-start gap-2 rounded-md p-1.5 text-sm hover:bg-white dark:hover:bg-zinc-900"
                    >
                      <BookOpen className="mt-0.5 h-4 w-4 shrink-0 text-indigo-500" aria-hidden />
                      <span className="min-w-0">
                        <span className="block font-medium text-zinc-900 dark:text-zinc-100">{a.title}</span>
                        {a.summary && <span className="block truncate text-xs text-zinc-500">{a.summary}</span>}
                      </span>
                    </a>
                  </li>
                ))}
              </ul>
            ) : (
              !suggestions.isFetching && <p className="text-xs text-zinc-500">No matching articles — go ahead and submit.</p>
            )}
          </section>
        )}
        <Field label="Description">
          {(id) => (
            <Textarea
              id={id}
              rows={5}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What happened, what did you expect, and any error messages."
              required
            />
          )}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Priority">
            {(id) => (
              <Select id={id} value={priority} onChange={(e) => setPriority(e.target.value as TicketPriority)}>
                {TICKET_PRIORITIES.map((p) => (
                  <option key={p}>{p}</option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Tags (optional)" hint="Comma-separated">
            {(id) => <Input id={id} value={tags} onChange={(e) => setTags(e.target.value)} placeholder="sso, azure-ad" />}
          </Field>
        </div>
      </form>
    </Dialog>
  );
}
