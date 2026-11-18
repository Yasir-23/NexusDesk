import { useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { cn } from '../../lib/cn';

/** Accessible on/off switch (`role="switch"`). */
export function Switch({
  checked,
  onChange,
  label,
  disabled,
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  /** Accessible name (not rendered). */
  label: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors disabled:opacity-50',
        checked ? 'bg-emerald-500' : 'bg-zinc-300 dark:bg-zinc-700',
      )}
    >
      <span
        className={cn(
          'inline-block h-4 w-4 rounded-full bg-white shadow transition-transform',
          checked ? 'translate-x-4.5' : 'translate-x-0.5',
        )}
        aria-hidden
      />
    </button>
  );
}

/**
 * Quota meter: a single-value bar against a limit. Colour switches to amber at 80% and rose
 * at 100%, always paired with the numeric reading so state is never colour-only.
 */
export function UsageMeter({
  label,
  used,
  limit,
  percentage,
  format = (n) => n.toLocaleString('en-US'),
  testId,
}: {
  label: string;
  used: number;
  limit: number;
  percentage: number;
  format?: (n: number) => string;
  testId?: string;
}) {
  if (limit < 0) {
    return (
      <div data-testid={testId}>
        <div className="flex items-baseline justify-between gap-3 text-sm">
          <span className="font-medium text-zinc-800 dark:text-zinc-200">{label}</span>
          <span className="tabular-nums text-zinc-600 dark:text-zinc-400">
            <span className="font-semibold text-zinc-900 dark:text-zinc-100">{format(used)}</span> · Unlimited
          </span>
        </div>
      </div>
    );
  }
  const pct = Math.max(0, Math.min(100, Number.isFinite(percentage) ? percentage : 0));
  const tone = pct >= 100 ? 'bg-rose-500' : pct >= 80 ? 'bg-amber-500' : 'bg-indigo-500';
  return (
    <div data-testid={testId}>
      <div className="mb-1.5 flex items-baseline justify-between gap-3 text-sm">
        <span className="font-medium text-zinc-800 dark:text-zinc-200">{label}</span>
        <span className="tabular-nums text-zinc-600 dark:text-zinc-400">
          <span className="font-semibold text-zinc-900 dark:text-zinc-100">{format(used)}</span> / {format(limit)}
          <span className="ml-2 text-xs text-zinc-500">{pct.toFixed(pct < 10 && pct > 0 ? 1 : 0)}%</span>
        </span>
      </div>
      <div
        role="progressbar"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={limit}
        aria-valuenow={used}
        aria-valuetext={`${format(used)} of ${format(limit)} (${pct.toFixed(0)}%)`}
        className="h-2 overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800"
      >
        <div className={cn('h-full rounded-full transition-[width] duration-500', tone)} style={{ width: `${pct}%` }} />
      </div>
      {pct >= 100 && <p className="mt-1 text-xs font-medium text-rose-600 dark:text-rose-400">Quota reached — upgrade to add more.</p>}
    </div>
  );
}

/** Pretty-prints a JSON string; falls back to the raw text. */
export function prettyJson(raw: string | null | undefined): string {
  if (!raw) return '';
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

export function JsonBlock({ json, className }: { json: string | null | undefined; className?: string }) {
  return (
    <pre
      className={cn(
        'max-h-72 overflow-auto rounded-lg bg-zinc-950 p-3 font-mono text-[11px] leading-relaxed text-zinc-100 dark:bg-black/60',
        className,
      )}
    >
      {prettyJson(json) || '{}'}
    </pre>
  );
}

/** Disclosure row toggle for expandable table rows. */
export function ExpandButton({ open, onClick, label }: { open: boolean; onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-expanded={open}
      aria-label={label}
      className="rounded p-0.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800"
    >
      <ChevronRight className={cn('h-4 w-4 transition-transform', open && 'rotate-90')} />
    </button>
  );
}

/** Local open/closed set for expandable rows. */
export function useExpanded() {
  const [open, setOpen] = useState<Set<string>>(() => new Set());
  return {
    isOpen: (id: string) => open.has(id),
    toggle: (id: string) =>
      setOpen((prev) => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id);
        else next.add(id);
        return next;
      }),
  };
}
