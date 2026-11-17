import type { ReactNode } from 'react';
import { AlertCircle, Loader2 } from 'lucide-react';
import { ApiProblemError } from '../../lib/api';
import { cn } from '../../lib/cn';

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={cn('h-4 w-4 animate-spin text-zinc-400', className)} aria-label="Loading" />;
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('animate-pulse rounded-md bg-zinc-200/70 dark:bg-zinc-800', className)} />;
}

export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon?: ReactNode;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-12 text-center">
      {icon && <div className="mb-3 rounded-full bg-zinc-100 p-3 text-zinc-400 dark:bg-zinc-800">{icon}</div>}
      <p className="text-sm font-medium text-zinc-900 dark:text-zinc-100">{title}</p>
      {description && <p className="mt-1 max-w-sm text-sm text-zinc-500 dark:text-zinc-400">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

/** Inline rendering of an RFC 7807 problem (title, detail, code, status). */
export function ErrorPanel({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const p = error instanceof ApiProblemError ? error : null;
  return (
    <div className="flex items-start gap-3 rounded-lg border border-rose-200 bg-rose-50 p-4 text-sm dark:border-rose-500/30 dark:bg-rose-500/10">
      <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-rose-500" aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="font-medium text-rose-800 dark:text-rose-200">{p?.title ?? 'Something went wrong'}</p>
        <p className="mt-0.5 text-rose-700 dark:text-rose-300">{p ? p.userMessage : String(error)}</p>
        {p && (
          <p className="mt-1 font-mono text-[11px] text-rose-500/80">
            {p.code ? `${p.code} · ` : ''}HTTP {p.status || 'network'}
          </p>
        )}
      </div>
      {onRetry && (
        <button type="button" onClick={onRetry} className="text-xs font-medium text-rose-700 underline dark:text-rose-300">
          Retry
        </button>
      )}
    </div>
  );
}
