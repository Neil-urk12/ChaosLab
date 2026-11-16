// Bearer-token HTTP client for the ChaosLab API. No React Native imports, so it runs under `node --test`.

export type Tokens = { accessToken: string; refreshToken: string };
export type TokenStore = { get(): Promise<Tokens | null>; set(tokens: Tokens | null): Promise<void> };

export class ApiError extends Error {
  status: number;
  constructor(status: number) {
    super(`HTTP ${status}`);
    this.status = status;
  }
}

export function createClient(baseUrl: string, store: TokenStore, onSignedOut: () => void, fetcher: typeof fetch = fetch) {
  let refreshing: Promise<Tokens | null> | null = null;

  const send = (path: string, method: string, token?: string, body?: unknown) =>
    fetcher(baseUrl + path, {
      method,
      headers: {
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });

  // Saves the tokens of a successful /token or /refresh answer; null means the credentials were rejected.
  async function tokensFrom(res: Response): Promise<Tokens | null> {
    if (res.status === 401) return null;
    if (!res.ok) throw new ApiError(res.status);
    const { accessToken, refreshToken } = await res.json();
    const tokens = { accessToken, refreshToken };
    await store.set(tokens);
    return tokens;
  }

  // Requests that hit a 401 together share one refresh instead of each spending the refresh token.
  const refresh = (refreshToken: string) =>
    (refreshing ??= send('/api/auth/refresh', 'POST', undefined, { refreshToken })
      .then(tokensFrom)
      .finally(() => (refreshing = null)));

  async function signedOut(): Promise<never> {
    await store.set(null);
    onSignedOut();
    throw new ApiError(401);
  }

  // An authenticated request: on a 401 it refreshes once and retries; a rejected refresh signs the user out.
  // A network failure throws without touching the stored tokens, so being offline never signs anyone out.
  async function request<T = void>(path: string, method = 'GET'): Promise<T> {
    let tokens = await store.get();
    if (!tokens) return signedOut();
    let res = await send(path, method, tokens.accessToken);
    if (res.status === 401) {
      tokens = await refresh(tokens.refreshToken);
      if (!tokens) return signedOut();
      res = await send(path, method, tokens.accessToken);
      if (res.status === 401) return signedOut();
    }
    if (!res.ok) throw new ApiError(res.status);
    return (res.status === 204 || res.headers.get('content-length') === '0' ? undefined : await res.json()) as T;
  }

  // False when the server rejects the credentials (wrong password, unknown or locked account).
  const login = async (identifier: string, password: string) =>
    (await tokensFrom(await send('/api/auth/token', 'POST', undefined, { identifier, password }))) !== null;

  // The server call revokes this user's refresh tokens on every device; the local tokens go even if it fails.
  async function logout() {
    await request('/api/auth/logout', 'POST').catch(() => {});
    await store.set(null);
    onSignedOut();
  }

  return { request, login, logout };
}
