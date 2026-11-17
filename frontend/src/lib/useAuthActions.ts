import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiProblemError, notifyError } from './api';
import { DEMO_PASSWORD, type DemoAccount } from './demo';
import { homePathFor } from './navigation';
import { login } from './queries';
import { useAuthStore } from '../stores/auth';
import { toast } from '../stores/toast';

export function useAuthActions() {
  const navigate = useNavigate();

  /** Logs in, wipes cached data from the previous identity, and routes to the role's home. */
  const signIn = useCallback(
    async (email: string, password: string, subdomain: string | null, redirectTo?: string) => {
      // No cache wipe here: every query key carries the session scope, and SessionScopeSync
      // drops the previous identity's queries as soon as `login` stores the new session.
      // (Clearing *after* the new session was stored cancelled the new tenant's in-flight
      // requests and left mounted pages stuck on their skeletons until a reload.)
      const { user } = await login(email, password, subdomain);
      toast.success(`Signed in as ${user.fullName}`, `${user.role}${subdomain ? ` · ${subdomain}` : ' · platform'}`);
      navigate(redirectTo ?? homePathFor(user.role), { replace: true });
      return user;
    },
    [navigate],
  );

  /** One-click demo login. Errors are toasted; returns `false` on failure. */
  const signInAs = useCallback(
    async (account: DemoAccount) => {
      try {
        await signIn(account.email, DEMO_PASSWORD, account.subdomain);
        return true;
      } catch (err) {
        if (err instanceof ApiProblemError) notifyError(err);
        return false;
      }
    },
    [signIn],
  );

  const signOut = useCallback(() => {
    // SessionScopeSync removes every query of the signed-out identity.
    useAuthStore.getState().logout();
    navigate('/login', { replace: true });
  }, [navigate]);

  return { signIn, signInAs, signOut };
}
