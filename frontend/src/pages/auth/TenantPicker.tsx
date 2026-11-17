import { Globe } from 'lucide-react';
import { Input } from '../../components/ui';
import { KNOWN_TENANTS } from '../../lib/demo';
import { cn } from '../../lib/cn';

export const CUSTOM = '__custom__';
export const PLATFORM = '__platform__';

/**
 * Segmented tenant selector. `value` is a subdomain, `PLATFORM`, or `CUSTOM`
 * (in which case `customValue` holds the typed subdomain).
 */
export function TenantPicker({
  value,
  onChange,
  customValue,
  onCustomChange,
  allowPlatform,
  idPrefix,
}: {
  value: string;
  onChange: (v: string) => void;
  customValue: string;
  onCustomChange: (v: string) => void;
  allowPlatform?: boolean;
  idPrefix: string;
}) {
  const options = [
    ...(allowPlatform ? [{ value: PLATFORM, label: 'Platform' }] : []),
    ...KNOWN_TENANTS.map((t) => ({ value: t.subdomain, label: t.subdomain })),
    { value: CUSTOM, label: 'Custom…' },
  ];

  return (
    <div className="space-y-2">
      <div role="radiogroup" aria-label="Tenant subdomain" className="flex flex-wrap gap-1.5">
        {options.map((o) => {
          const active = value === o.value;
          return (
            <button
              key={o.value}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => onChange(o.value)}
              className={cn(
                'inline-flex h-8 items-center gap-1.5 rounded-lg border px-3 text-xs font-medium transition-colors',
                o.value !== PLATFORM && o.value !== CUSTOM && 'font-mono',
                active
                  ? 'border-indigo-500 bg-indigo-50 text-indigo-700 dark:border-indigo-400 dark:bg-indigo-500/15 dark:text-indigo-200'
                  : 'border-zinc-200 bg-white text-zinc-600 hover:border-zinc-300 hover:text-zinc-900 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100',
              )}
            >
              {o.value === PLATFORM && <Globe className="h-3.5 w-3.5" aria-hidden />}
              {o.label}
            </button>
          );
        })}
      </div>
      {value === CUSTOM && (
        <Input
          id={`${idPrefix}-custom-tenant`}
          aria-label="Custom tenant subdomain"
          placeholder="e.g. delta"
          value={customValue}
          onChange={(e) => onCustomChange(e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ''))}
          className="font-mono"
          autoFocus
        />
      )}
    </div>
  );
}

export function resolvePickedTenant(value: string, custom: string): string | null {
  if (value === PLATFORM) return null;
  if (value === CUSTOM) return custom.trim() || null;
  return value;
}
