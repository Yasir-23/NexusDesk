import { Download, FileText, Paperclip, Upload } from 'lucide-react';
import { useRef, useState, type ChangeEvent } from 'react';
import { Button, Card, CardBody, CardHeader, ErrorPanel, Skeleton } from '../../components/ui';
import { ApiProblemError, notifyError } from '../../lib/api';
import { formatBytes, formatRelative } from '../../lib/format';
import { ATTACHMENT_EXTENSIONS, downloadAttachment, useAttachments, useUploadAttachment, validateAttachment } from '../../lib/tickets';
import type { TicketAttachment } from '../../lib/types';
import { toast } from '../../stores/toast';

/** Lists, uploads (`multipart/form-data`, field `file`) and downloads (authenticated blob) attachments. */
export function AttachmentsPanel({ ticketId }: { ticketId: string }) {
  const { data, isLoading, error, refetch } = useAttachments(ticketId);
  const upload = useUploadAttachment(ticketId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [downloading, setDownloading] = useState<string | null>(null);

  const onPick = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    const problem = validateAttachment(file);
    if (problem) {
      toast.error('File not accepted', problem);
      return;
    }
    try {
      const saved = await upload.mutateAsync(file);
      toast.success('Attachment uploaded', `${saved.fileName} · ${formatBytes(saved.sizeBytes)}`);
    } catch {
      /* api layer already toasted the ProblemDetails */
    }
  };

  const onDownload = async (a: TicketAttachment) => {
    setDownloading(a.id);
    try {
      await downloadAttachment(a);
    } catch (err) {
      if (err instanceof ApiProblemError) notifyError(err);
      else toast.error('Download failed', String(err));
    } finally {
      setDownloading(null);
    }
  };

  return (
    <Card>
      <CardHeader
        title="Attachments"
        description={`${ATTACHMENT_EXTENSIONS.join(' ')} · max 10 MB`}
        icon={<Paperclip className="h-4 w-4" />}
        actions={
          <>
            <input
              ref={inputRef}
              type="file"
              className="sr-only"
              aria-label="Attachment file"
              accept={ATTACHMENT_EXTENSIONS.join(',')}
              onChange={onPick}
            />
            <Button size="sm" variant="secondary" icon={<Upload className="h-3.5 w-3.5" />} loading={upload.isPending} onClick={() => inputRef.current?.click()}>
              Upload
            </Button>
          </>
        }
      />
      <CardBody className="p-0">
        {error ? (
          <div className="p-4">
            <ErrorPanel error={error} onRetry={() => void refetch()} />
          </div>
        ) : isLoading ? (
          <div className="p-4">
            <Skeleton className="h-8 w-full" />
          </div>
        ) : !data?.length ? (
          <p className="px-5 py-4 text-xs text-zinc-500">No files attached yet.</p>
        ) : (
          <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
            {data.map((a) => (
              <li key={a.id} data-testid={`attachment-${a.fileName}`} className="flex items-center gap-3 px-5 py-2.5">
                <FileText className="h-4 w-4 shrink-0 text-zinc-400" aria-hidden />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium" title={a.fileName}>
                    {a.fileName}
                  </p>
                  <p className="text-[11px] text-zinc-500">
                    {formatBytes(a.sizeBytes)} · {a.contentType} · {formatRelative(a.createdAtUtc)}
                  </p>
                </div>
                <Button
                  size="icon"
                  variant="ghost"
                  aria-label={`Download ${a.fileName}`}
                  loading={downloading === a.id}
                  icon={downloading === a.id ? undefined : <Download className="h-4 w-4" />}
                  onClick={() => void onDownload(a)}
                />
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}
