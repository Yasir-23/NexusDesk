import { cn } from '../../lib/cn';

export function Logo({ className, inverted }: { className?: string; inverted?: boolean }) {
  return (
    <span className={cn('flex items-center gap-2', className)}>
      <svg viewBox="0 0 32 32" className="h-7 w-7" aria-hidden>
        <rect width="32" height="32" rx="8" className="fill-indigo-600" />
        <path d="M9 23V9l14 14V9" stroke="#fff" strokeWidth="3" fill="none" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      <span className={cn('text-[15px] font-semibold tracking-tight', inverted ? 'text-white' : 'text-zinc-900 dark:text-zinc-50')}>
        NexusDesk
      </span>
    </span>
  );
}
