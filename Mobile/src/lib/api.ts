import * as SecureStore from 'expo-secure-store';
import { useCallback, useEffect, useState } from 'react';

import { ApiError, createClient, type Tokens } from './client';

// shortcut: one instance per build, set at build time; add a server field to the login screen if the app is ever
// distributed to more than one ChaosLab instance.
export const baseUrl = process.env.EXPO_PUBLIC_API_URL?.replace(/\/+$/, '') ?? '';

// Two keys, not one JSON value: some iOS versions reject keychain values over 2048 bytes.
const ACCESS = 'chaoslab.accessToken';
const REFRESH = 'chaoslab.refreshToken';

const cache = new Map<string, unknown>();
const signedOutListeners = new Set<() => void>();

export const onSignedOut = (listener: () => void) => {
  signedOutListeners.add(listener);
  return () => void signedOutListeners.delete(listener);
};

export const api = createClient(
  baseUrl,
  {
    async get(): Promise<Tokens | null> {
      const [accessToken, refreshToken] = await Promise.all([SecureStore.getItemAsync(ACCESS), SecureStore.getItemAsync(REFRESH)]);
      return accessToken && refreshToken ? { accessToken, refreshToken } : null;
    },
    async set(tokens) {
      if (tokens) await Promise.all([SecureStore.setItemAsync(ACCESS, tokens.accessToken), SecureStore.setItemAsync(REFRESH, tokens.refreshToken)]);
      else await Promise.all([SecureStore.deleteItemAsync(ACCESS), SecureStore.deleteItemAsync(REFRESH)]);
    },
  },
  () => {
    // The next user must not see the previous user's cached results.
    cache.clear();
    signedOutListeners.forEach((listener) => listener());
  },
);

// Loads a GET resource. Cached data shows at once while a fresh copy loads, so going back to a screen or opening
// a detail of an already loaded run needs no spinner. `whenMissing` stands in for a 404 answer.
export function useLoad<T>(path: string, whenMissing?: T) {
  const [data, setData] = useState<T | undefined>(() => cache.get(path) as T | undefined);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(
    () =>
      api.request<T>(path).then(
        (value) => {
          cache.set(path, value);
          setData(value);
          setError(null);
        },
        (e) => {
          const status = e instanceof ApiError ? e.status : 0;
          if (status === 404 && whenMissing !== undefined) setData(whenMissing);
          else if (status === 401) return; // Signed out; the layout swaps to the login screen.
          else setError(status === 404 ? 'This no longer exists, or you no longer have access to it.' : 'Could not load. Pull down to try again.');
        },
      ),
    // whenMissing is a constant per call site; listing it would reload on every render when it is a literal.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [path],
  );

  useEffect(() => {
    load();
  }, [load]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    await load();
    setRefreshing(false);
  }, [load]);

  return { data, error, refreshing, refresh };
}
