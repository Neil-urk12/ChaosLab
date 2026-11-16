# ChaosLab mobile viewer

A read-only viewer for ChaosLab results, built with Expo (React Native, TypeScript). It signs in with the
backend's bearer-token endpoints and only reads: projects, a project's overview, and a run's score, findings,
scenario results, and failure timelines. Targets, specifications, scenarios, and runs are managed in the web app.

## Run it

```sh
cp .env.example .env.local   # then set EXPO_PUBLIC_API_URL
pnpm install
pnpm start                   # scan the QR code with Expo Go, or press a for an Android emulator
```

The phone has to reach the backend. `dotnet run` listens on localhost only, so for a real phone start the
backend on every interface instead:

```sh
cd ../Backend && dotnet run --urls http://0.0.0.0:5274
```

Standalone Android builds block plain HTTP. Put the instance behind HTTPS before building one.

## Check it

```sh
pnpm test        # API client: token refresh, retry, sign-out
pnpm run check   # typecheck
pnpm run lint
```

## Layout

- `src/app/` holds the screens (Expo Router): login, projects, project, run, and scenario and finding detail.
- `src/lib/client.ts` is the HTTP client. It refreshes the access token once on a 401 and retries.
- `src/lib/api.ts` binds the client to the device's secure storage and provides `useLoad`.
- `@web/*` imports come from `../Frontend/src/lib`. The response types and the label, format, and scoring
  helpers are shared with the web app, not copied.
