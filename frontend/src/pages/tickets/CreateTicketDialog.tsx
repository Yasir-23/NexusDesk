import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Dialog, ErrorPanel, Field, Input, Select, Textarea } from '../../components/ui';
import { useCreateTicket, useTenantUsers } from '../../lib/tickets';
import { TICKET_PRIORITIES, type TicketChannel, type TicketPriority } from '../../lib/types';
import { toast } from '../../stores/toast';

const CHANNELS: TicketChannel[] = ['WebPortal', 'Email', 'Api', 'LiveChat'];

/** `POST /api/v1/tickets` with the customer picked from `GET /api/v1/users`. */
export function CreateTicketDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate();
  const users = useTenantUsers(open);
  const customers = (users.data ?? []).filter((u) => u.role === 'Customer' && u.isActive && !u.isAnonymized);
  const create = useCreateTicket();

  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<TicketPriority>('Medium');
  const [channel, setChannel] = useState<TicketChannel>('WebPortal');
  const [customerId, setCustomerId] = useState('');
  const [tags, setTags] = useState('');

  // Pre-select the first customer so the demo is one click away.
  useEffect(() => {
    if (open && !customerId && customers.length > 0) setCustomerId(customers[0].id);
  }, [open, customerId, customers]);

  const reset = () => {
    setTitle('');
    setDescription('');
    setPriority('Medium');
    setChannel('WebPortal');
    setTags('');
    create.reset();
  };

  const close = () => {
    reset();
    onClose();
  };

  const canSubmit = title.trim().length > 0 && description.trim().length > 0 && Boolean(customerId);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    try {
      const ticket = await create.mutateAsync({
        title: title.trim(),
        description: description.trim(),
        priority,
        channel,
        customerId,
        tags: tags
          .split(',')
          .map((t) => t.trim().toLowerCase())
          .filter(Boolean),
      });
      toast.success(`Ticket ${ticket.ticketNumber} created`, ticket.title);
      close();
      navigate(`/tickets/${ticket.id}`);
    } catch {
      /* rendered inline below */
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      title="Create ticket"
      description="Opens a ticket on behalf of a customer. SLA targets are applied from the tenant's policy for the chosen priority."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form="create-ticket-form" loading={create.isPending} disabled={!canSubmit}>
            Create ticket
          </Button>
        </>
      }
    >
      <form id="create-ticket-form" onSubmit={submit} className="space-y-4">
        {create.error && <ErrorPanel error={create.error} />}
        <Field label="Customer" hint={users.isLoading ? 'Loading tenant users…' : undefined}>
          {(id) => (
            <Select id={id} value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
              {customers.length === 0 && <option value="">No customers found</option>}
              {customers.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.fullName} · {c.email}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Title">
          {(id) => (
            <Input id={id} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Cannot access billing portal from Chrome" required />
          )}
        </Field>
        <Field label="Description">
          {(id) => (
            <Textarea
              id={id}
              rows={4}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Receiving a blank page and 403 Forbidden after MFA prompt."
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
          <Field label="Channel">
            {(id) => (
              <Select id={id} value={channel} onChange={(e) => setChannel(e.target.value as TicketChannel)}>
                {CHANNELS.map((c) => (
                  <option key={c}>{c}</option>
                ))}
              </Select>
            )}
          </Field>
        </div>
        <Field label="Tags" hint="Comma-separated, e.g. billing, auth">
          {(id) => <Input id={id} value={tags} onChange={(e) => setTags(e.target.value)} placeholder="billing, auth, chrome" />}
        </Field>
      </form>
    </Dialog>
  );
}
