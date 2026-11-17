import { Check, ChevronDown, LogOut, Repeat, UserCircle } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { RoleBadge } from '../domain/Badges';
import { Spinner } from '../ui';
import { MenuItem, MenuLabel, Popover } from '../ui/Popover';
import { DEMO_ACCOUNTS } from '../../lib/demo';
import { useAuthActions } from '../../lib/useAuthActions';
import { useAuthStore } from '../../stores/auth';
import { cn } from '../../lib/cn';

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join('');
}

/** User menu with the one-click demo role switcher. */
export function AccountMenu() {
  const user = useAuthStore((s) => s.user);
  const { signInAs, signOut } = useAuthActions();
  const [pending, setPending] = useState<string | null>(null);
  if (!user) return null;

  return (
    <Popover
      className="w-80"
      trigger={({ toggle, open }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label="Account menu"
          className="flex h-9 items-center gap-2 rounded-lg pl-1 pr-2 transition-colors hover:bg-zinc-100 dark:hover:bg-zinc-800"
        >
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-500 text-[11px] font-semibold text-white">
            {initials(user.fullName)}
          </span>
          <span className="hidden text-left leading-tight sm:block">
            <span className="block max-w-36 truncate text-xs font-medium text-zinc-900 dark:text-zinc-100">{user.fullName}</span>
            <span className="block text-[11px] text-zinc-500">{user.role}</span>
          </span>
          <ChevronDown className="h-3.5 w-3.5 text-zinc-400" aria-hidden />
        </button>
      )}
    >
      {(close) => (
        <div>
          <div className="px-2.5 py-2">
            <p className="truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{user.fullName}</p>
            <p className="truncate text-xs text-zinc-500">{user.email}</p>
            <div className="mt-1.5">
              <RoleBadge role={user.role} />
            </div>
          </div>
          <div className="border-t border-zinc-100 pt-1 dark:border-zinc-800">
            <MenuLabel>
              <span className="inline-flex items-center gap-1.5">
                <Repeat className="h-3 w-3" /> Quick switch demo account
              </span>
            </MenuLabel>
            {DEMO_ACCOUNTS.map((a) => {
              const current = a.email === user.email;
              return (
                <MenuItem
                  key={a.email}
                  active={current}
                  disabled={pending !== null}
                  onClick={async () => {
                    if (current) return close();
                    setPending(a.email);
                    const ok = await signInAs(a);
                    setPending(null);
                    if (ok) close();
                  }}
                >
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-xs font-medium">{a.email}</span>
                    <span className="block text-[11px] text-zinc-500">
                      {a.role} · {a.subdomain ?? 'platform'}
                    </span>
                  </span>
                  {pending === a.email ? <Spinner /> : current ? <Check className="h-4 w-4" /> : null}
                </MenuItem>
              );
            })}
          </div>
          <div className="mt-1 border-t border-zinc-100 pt-1 dark:border-zinc-800">
            <Link
              to="/account"
              onClick={close}
              className={cn(
                'flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-zinc-700 hover:bg-zinc-100 dark:text-zinc-300 dark:hover:bg-zinc-800',
              )}
            >
              <UserCircle className="h-4 w-4" /> My profile
            </Link>
            <MenuItem
              onClick={() => {
                close();
                signOut();
              }}
            >
              <LogOut className="h-4 w-4" /> Sign out
            </MenuItem>
          </div>
        </div>
      )}
    </Popover>
  );
}
