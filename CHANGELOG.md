# Changelog

All notable changes to ChaosLab are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). No versions have been
released yet, so history is grouped by date.

## [Unreleased]

### Added

- Environment variable templates for Development, Testing, Staging, and Production,
  with database connection placeholders and instructions for loading a local `.env`.
- `ConnectionStrings:DefaultConnection` placeholders for Development, Testing,
  Staging, and Production, with README instructions for supplying connection strings
  through environment variables or deployment secrets.
- First-run SuperAdmin setup through `POST /api/auth/setup`: on an uninitialized
  database the backend prints a setup link carrying a random 64-character token in
  the URL fragment. The token lives only in backend memory, expires one hour after
  generation, and is compared in constant time; restarting an uninitialized backend
  issues a new link and invalidates the old one. The endpoint is anonymous, returns
  `403` for a missing, invalid, or expired token, and `409` once the `SuperAdmin`
  role exists. Role and account are created in one transaction under a PostgreSQL
  advisory lock, so simultaneous requests cannot both initialize the instance. The
  role is the durable setup marker, so deleting or disabling the administrator never
  reopens installation. The new administrator is signed in immediately and is not
  forced to change the password just chosen.
- Startup validation of `BootstrapSetup:PublicUrl`, requiring an absolute HTTP(S) URL
  without credentials, query, or fragment because the setup link appends
  `/install#token=...` to it. Invalid configuration fails the backend at startup.
- `GET /api/projects`, the first product endpoint, listing active projects newest
  first through a read-only projection into a response DTO, limited to projects
  where the caller has an active membership.
- `POST /api/projects`, creating a project from `name` and optional `description`,
  rejecting a missing name or one longer than 200 characters with a validation
  problem, and returning `201 Created` with a `Location` header. The creator is added
  as the project's `Owner`.
- Local development environment loading: the backend reads `Backend/.env.local` at
  startup through `DotNetEnv` with `NoClobber`, so exported variables and the pod
  secret stay authoritative. The file is gitignored and excluded from the container
  build context.
- `make dev-be` target for running the backend dev server.
- Authentication with ASP.NET Core Identity: `POST /api/auth/login` sets an HttpOnly,
  Secure, SameSite=Lax cookie for web clients; `POST /api/auth/token` and
  `POST /api/auth/refresh` issue Identity bearer tokens (15-minute access token) for
  mobile clients. `GET /api/auth/me`, `POST /api/auth/change-password`, and
  `POST /api/auth/logout` complete the flow. Every endpoint requires sign-in by default,
  and an account with `MustChangePassword` can reach only `me`, `change-password`, and
  `logout` until it replaces its password. Failed logins count toward Identity lockout.
  Login accepts a username or an email (an email shared by two accounts is rejected).
  Logging out with a bearer token rotates the security stamp, revoking the user's
  refresh tokens on every device. Sign-ins, failures, lockouts, refreshes, password
  changes, and logouts are logged by user id, never by the submitted identifier.
- Wire up login page to the backend.
- `(auth)/install` page that redeems the setup token from the URL fragment, validates
  its format, submits the first administrator's details to `/api/auth/setup`, and
  replaces the history entry so Back does not return to the secret link.
- `(app)` route group with a client-side session guard (`ssr = false`) that redirects
  unauthenticated visitors to `/login` and pending password changes to `/setup`, plus
  a sidebar shell and an overview page listing the caller's recent projects.
- PostgreSQL integration test suite in `Backend.Tests`: real PostgreSQL via
  Testcontainers (no EF InMemory), `BackendFactory` for HTTP tests through
  `WebApplicationFactory<Program>`, and tests covering model invariants, check
  constraints, and the initial-admin bootstrap, plus `Backend.Tests/README.md`.
- Workspace overview and project detail dashboards: `GET /api/projects/overview`
  aggregates project, endpoint, and run counts, average and trending resilience
  scores, failed findings, and recent activity across the caller's projects;
  `GET /api/projects/{id}` returns one project's dashboard with its latest score,
  breakdown, spec, members, findings, and recent runs (non-members get `404` so
  project ids are not disclosed). The frontend adds the overview and
  `projects/[id]` pages with a `ScoreRing` component, date/score formatting
  helpers, and a typed `src/lib/api.ts` client.
- Projects inventory page at `/projects`, backed by an extended `GET /api/projects`
  response that adds the default environment name and base URL, active scenario
  count, last run time, and latest resilience score per project.
- Light mode theme toggle in the sidebar: a semantic color palette
  (`--color-canvas`, `--color-panel`, `--color-line`, ...) in `layout.css` with
  light values under `[data-theme='light']`, an inline script in `app.html` that
  sets the stored or system theme before first paint so the page never flashes the
  wrong theme, and a toggle that remembers the choice in `localStorage`.
- `make start` recipe for running the backend and frontend together outside the
  kube pod against the deployed database.
- `make migrate` recipe for applying EF migrations to the database configured in
  `Backend/.env.local`.
- Highlighted first-run setup link in the backend console: a framed, ANSI-colored
  block with the expiry note, falling back to plain text when output is redirected
  or `NO_COLOR` is set (so pod logs stay clean).
- `PROJECT.md`, the full product specification, which merges the earlier project
  notes and context export with the current state of the repository and collects
  undecided items in section 25.
- OpenAPI specification import: `POST /api/projects/{id}/spec` accepts a pasted
  document, an uploaded file, or a URL the backend fetches once (15-second timeout,
  5 MB limit, at most 3 redirects, proxy environment variables ignored). Every
  resolved address is checked at connect time, and link-local (including cloud
  metadata), unspecified, and multicast addresses are refused, as are the AWS IPv6
  and Alibaba Cloud metadata hosts and NAT64 forms of any blocked IPv4 address;
  private and loopback hosts stay reachable because test targets usually live there. OpenAPI 3.x JSON and YAML are read with `Microsoft.OpenApi`, and
  Swagger 2.0 is converted to 3.0. Unresolved local `$ref`s, undeclared path
  parameters, and parser or validation errors block the import and come back as a
  `422` validation report; OAuth2 and OpenID Connect schemes produce a warning. A
  successful import replaces the active spec, and earlier versions stay for
  historical runs. Viewers get `403`.
- `GET /api/projects/{id}/spec` lists the active spec's endpoints with tag, effective
  security, request body, response codes, scenario count, and testing choice, and
  marks operations added or modified since the previous import along with the ones
  removed. `GET /api/projects/{id}/spec/document` returns the original document as
  plain text.
- Per-endpoint testing choice (`Included`, `Excluded`, `NeverTest`) stored in the new
  `ApiEndpoints.Testing` column (migration `AddEndpointTesting`), set through
  `PATCH /api/projects/{id}/spec/endpoints/{endpointId}` and carried over to the
  matching path and method on re-import.
- API specification page at `/projects/[id]/spec` with import by paste, file upload
  or drag-and-drop, or URL, a validation report that can be downloaded, the
  discovered endpoints table with search, testing filters, and pagination, and a
  summary of the last re-import. Viewers see the page read-only, without import,
  re-import, or testing controls. The project header and tabs moved into a shared
  `projects/[id]` layout, and the overview's "View specification" button now opens
  the page.

### Changed

- The `(auth)/setup` page now changes a password for real against
  `/api/auth/change-password` (current password, new password, confirmation) instead
  of being an unwired placeholder for the initial administrator credentials.
- Bundle the Inter font with the frontend (`@fontsource-variable/inter`) instead of
  loading it from Google Fonts, so auth pages make no third-party requests and work
  offline. The font is imported once in the root layout. The package is a
  `devDependency`, like the rest of the frontend, because Vite bundles it at build time.
- Drop the unneeded `!` important modifier from the submit-button margins on the
  login and setup pages. Tailwind v4 `space-y-*` no longer styles the last child.
- Backend HTTP features are organized as vertical slices under `Backend/Features/`,
  one file per query or command, with routes grouped under `/api` in `Program.cs`.
  `Features/Projects` is the reference slice.
- Rewrote `FeaturesFlow.md` to match `PROJECT.md`, tagging each process as Done,
  Partial, or Planned.

### Removed

- The startup `InitialAdminBootstrapper` and its `BootstrapAdmin:*` configuration;
  initial administration now comes from the one-time setup link, and obsolete
  `BootstrapAdmin__*` secrets can be removed from deployments.
- Google Fonts `<link>` tags from the `(auth)` layout.
- The template `weatherforecast` endpoint and its `WeatherForecast` record.

## 2026-10-26

### Added

- Setup and login auth pages under the `(auth)` route group, with a shared
  `PasswordField` component and `auth-input` / `auth-submit` component styles.

## 2026-10-13

### Changed

- Updated `FeaturesFlow.md`.

## 2026-10-04 – 2026-10-05

### Added

- Chaos-testing domain model: entities and enums for projects, specs, environments,
  endpoints, scenarios, test runs, observations, findings, recommendations and reports.
- PostgreSQL persistence through EF Core + Npgsql (`ApplicationDbContext`).
- Backend integration test wiring in `Backend.sln`.
- `InitialCreate` migration.

### Changed

- Organized entity members into sections.

## 2026-09-24 – 2026-09-27

### Added

- README, plus work-in-progress app and feature flow docs.

## 2026-09-13 – 2026-09-20

### Added

- .NET 10 backend (non-AOT) with the Scalar API reference for OpenAPI.
- SvelteKit frontend.
- Podman `kube play` dev stack (`backend-pod.yaml`, `Makefile`, `secret.yaml.example`).
- Makefile target to run the frontend dev server.
- EF Core dependency.
