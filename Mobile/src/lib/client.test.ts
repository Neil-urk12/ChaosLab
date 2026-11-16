// Run with `pnpm test` (node --test). Covers the refresh-and-retry path of the API client.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ApiError, createClient, type Tokens } from './client.ts';

function setup(handler: (url: string, token: string | null) => Response) {
  let tokens: Tokens | null = { accessToken: 'old', refreshToken: 'r1' };
  const calls: string[] = [];
  let signedOut = 0;
  const client = createClient(
    'http://api',
    { get: async () => tokens, set: async (next) => void (tokens = next) },
    () => void signedOut++,
    (async (url: string, init: RequestInit) => {
      calls.push(url.replace('http://api', ''));
      const auth = (init.headers as Record<string, string>).Authorization ?? null;
      return handler(url, auth?.replace('Bearer ', '') ?? null);
    }) as typeof fetch,
  );
  return { client, calls, tokens: () => tokens, signedOut: () => signedOut };
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

test('an expired access token is refreshed once and the request retried, even for concurrent requests', async () => {
  const s = setup((url, token) => {
    if (url.endsWith('/api/auth/refresh')) return json({ accessToken: 'new', refreshToken: 'r2' });
    return token === 'new' ? json({ ok: true }) : new Response(null, { status: 401 });
  });

  const results = await Promise.all([s.client.request('/api/projects'), s.client.request('/api/auth/me')]);

  assert.deepEqual(results, [{ ok: true }, { ok: true }]);
  assert.equal(s.calls.filter((path) => path === '/api/auth/refresh').length, 1);
  assert.deepEqual(s.tokens(), { accessToken: 'new', refreshToken: 'r2' });
  assert.equal(s.signedOut(), 0);
});

test('a rejected refresh clears the tokens and signs the user out', async () => {
  const s = setup(() => new Response(null, { status: 401 }));

  await assert.rejects(s.client.request('/api/projects'), (error) => error instanceof ApiError && error.status === 401);

  assert.equal(s.tokens(), null);
  assert.equal(s.signedOut(), 1);
});

test('a server error during refresh keeps the tokens, so an outage does not sign anyone out', async () => {
  const s = setup((url) => new Response(null, { status: url.endsWith('/refresh') ? 503 : 401 }));

  await assert.rejects(s.client.request('/api/projects'), (error) => error instanceof ApiError && error.status === 503);

  assert.deepEqual(s.tokens(), { accessToken: 'old', refreshToken: 'r1' });
  assert.equal(s.signedOut(), 0);
});

test('login stores the tokens, and wrong credentials store nothing', async () => {
  const s = setup((_, token) => (token === null ? json({ accessToken: 'a', refreshToken: 'r' }) : json({})));
  assert.equal(await s.client.login('admin', 'secret'), true);
  assert.deepEqual(s.tokens(), { accessToken: 'a', refreshToken: 'r' });

  const rejected = setup(() => new Response(null, { status: 401 }));
  assert.equal(await rejected.client.login('admin', 'wrong'), false);
  assert.deepEqual(rejected.tokens(), { accessToken: 'old', refreshToken: 'r1' });
});
