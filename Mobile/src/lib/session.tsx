import { createContext, use, useCallback, useEffect, useState, type ReactNode } from 'react';

import { api, onSignedOut } from './api';
import { ApiError } from './client';

// GET /api/auth/me
export type Me = { id: string; userName: string; displayName: string; email: string | null; mustChangePassword: boolean; roles: string[] };

type State = { status: 'loading' | 'signedOut' | 'unreachable' } | { status: 'signedIn'; user: Me };

type Session = {
  state: State;
  // Resolves to an error message, or null once signed in.
  signIn(identifier: string, password: string): Promise<string | null>;
  signOut(): Promise<void>;
  retry(): void;
};

const SessionContext = createContext<Session | null>(null);

export const useSession = () => use(SessionContext)!;

export function SessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<State>({ status: 'loading' });

  // Stored tokens are only trusted once the server has answered for them.
  const loadUser = useCallback(
    () =>
      api.request<Me>('/api/auth/me').then(
        (user) => setState({ status: 'signedIn', user }),
        (e) => setState({ status: e instanceof ApiError && e.status === 401 ? 'signedOut' : 'unreachable' }),
      ),
    [],
  );

  useEffect(() => {
    loadUser();
    return onSignedOut(() => setState({ status: 'signedOut' }));
  }, [loadUser]);

  const signIn = useCallback(
    async (identifier: string, password: string) => {
      try {
        // One message for every rejection: the server does not say whether the account exists or is locked.
        if (!(await api.login(identifier.trim(), password))) return 'Wrong username or password, or the account is temporarily locked.';
      } catch {
        return 'Could not reach the server. Check your connection and try again.';
      }
      await loadUser();
      return null;
    },
    [loadUser],
  );

  const retry = useCallback(() => {
    setState({ status: 'loading' });
    loadUser();
  }, [loadUser]);

  return <SessionContext value={{ state, signIn, signOut: api.logout, retry }}>{children}</SessionContext>;
}
