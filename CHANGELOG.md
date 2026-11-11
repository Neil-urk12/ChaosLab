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
  `422` validation report; OAuth2 and OpenID Connect schemes produce a warning.
  Keys under `paths` that do not start with `/`, such as `x-` extensions, are not
  imported as operations, so a spec cannot point requests at another host. A
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
- Chaos scenario generation, started by the user. Importing a spec does not create
  scenarios; `POST /api/projects/{id}/scenarios/generate` does. It adds at most one
  scenario per applicable fault type for each endpoint, decided from its method,
  response bodies and declared status codes, security, and an `Idempotency-Key`
  header, and records why each one applies. After a re-import, generating carries
  edits over from the latest earlier spec that had scenarios, matched by path,
  method, and fault. Running it again re-applies the rules, keeping edits and only
  removing scenarios that no longer apply and that no run has used; it returns `409`
  when a simultaneous generation changed the same scenarios first.
  Throttle scenarios are on by default only where the spec declares `429`. Slow drip
  and dependency failure start disabled because their injectors are still proofs of
  concept.
- `GET /api/projects/{id}/scenarios` lists the scenarios of included endpoints, and
  `PATCH /api/projects/{id}/scenarios/{scenarioId}` changes a scenario's default,
  its parameters (validated against a fixed field list per fault type), or its
  structured invariants: status code in set, field equals, field not below, record
  count after duplicates (duplicate requests only, with a required read-back
  request), maximum duration, and required header present. A read-back is an
  optional `GET` path. A `null` entry in the invariants list is a validation error.
- Scenarios page at `/projects/[id]/scenarios` with a Generate scenarios button
  before the first generation (Regenerate afterwards), summary counts, fault filters,
  search, per-scenario enable toggles, and a detail panel for parameters and
  invariants. State-changing scenarios carry a test-data warning. Viewers see the
  page read-only.
- Target environment settings. `PUT /api/projects/{id}/environment` saves the
  project's default target (base URL, environment label, request timeout of 1 to
  300 seconds, TLS verification); the environment's name, shown in project lists and
  run rows, follows the label on every save. It requires the caller to confirm that they own
  or may test the API and that it is not production. The confirmation is recorded
  with the user and time, and again whenever the base URL changes. Moving the target
  to another scheme, host, or port clears every stored credential secret and deletes
  secret headers, so secrets are never sent to a host they were not entered for.
  `POST /api/projects/{id}/environment/check` tests reachability with the same
  address guard as the spec fetch. Two first saves of the same target at once
  return `409` instead of failing.
- Target credentials per spec security scheme (Bearer, API key, Basic, or none)
  under `/api/projects/{id}/environment/credentials/{scheme}`. Secrets are encrypted
  with AES-256-GCM using the new `Credentials:Key` setting (base64, 32 bytes), bound
  to their row, and only their last four characters are ever returned. Validation
  calls one included `GET` operation without path parameters that uses the scheme
  and records the result; it returns `422` when a stored secret no longer decrypts,
  for example after `Credentials:Key` changed, and records an undecryptable credential
  as invalid. API keys go in the query, the `Cookie` header, or a named
  header, as their scheme declares; a header API key replaces a global header of the
  same name. Bearer tokens and API keys must be ASCII, because HTTP header values
  cannot carry other characters. Requests to the target never follow redirects
  and are refused when an operation path would change the base URL's scheme, host,
  or port, so credentials stay with the target host.
- Global headers under `/api/projects/{id}/environment/headers`, sent with every
  scenario request or only with listed operations. Secret values are encrypted like
  credentials and cannot be made plain again; reserved headers such as `Host` and
  `Authorization` are refused. Values must be printable ASCII.
- Settings page at `/projects/[id]/settings` with the target, credentials, and global
  headers sections. Viewers see it read-only.
- `Credentials:Key` in the environment templates, `secret.yaml.example`
  (`credentials-key`), and the pod definition, where it is optional so existing
  secrets still start. Without the key, saving or validating a secret credential and
  adding a secret header value fail; plain headers and credentials of type none
  still work.
- Test run history: `GET /api/projects/{id}/runs` returns one page of a project's
  runs, newest first, with a per-project run number, the change from the previous
  completed run on the same environment and scoring version, and evidence coverage (Passed and Failed findings as a share of those
  plus Inconclusive). It filters on the server by `query` (run ID or person),
  `status`, `environment`, and `days`, and pages with `page` and `pageSize` (at most
  100). The Test runs tab at `/projects/[id]/runs` keeps these filters in the URL
  (a date range outside the menu falls back to the last 30 days), shows how many runs
  match when filters narrow the list, pages with first, last and nearby page links,
  and lets you select two runs on the same environment and scoring version for
  comparison. Starting runs and the comparison view are not built yet, so their
  buttons stay disabled.
- Findings: `GET /api/projects/{id}/findings` returns every finding of one run with
  its severity, endpoint, scenario fault, evidence and linked recommendation, plus
  the 50 newest runs that have findings. Without `?run=` it picks the newest
  completed run with findings. Any of the project's runs with findings can be
  requested, including ones older than that list; another project's run or a run
  without findings returns 404. The Findings
  tab at `/projects/[id]/findings` shows outcome counts, filters by search,
  outcome (Fail and Degraded by default), severity, category and endpoint, exports
  the filtered rows as CSV, and opens a detail panel with expected versus observed
  behavior, evidence, interpretation and the recommendation. Rerunning a scenario
  and the failure timeline are not built yet, so their buttons stay disabled.
- Resilience findings gained a `Degraded` outcome and a `Severity` column
  (migration `AddFindingSeverity`; existing findings default to Info).
- Project membership: `GET /api/projects/{id}/members` lists active and removed
  members with the caller's role, and owners can search existing accounts
  (`GET .../members/candidates?q=`), add a member (`POST`), change a role (`PATCH
  .../members/{userId}`) and remove a member (`DELETE`). Removal is a soft delete,
  and adding a removed person reactivates the same membership row, keeping its
  original join date. Adding someone who is already a member returns 409, including
  when two owners add the same person at once. The last owner can be neither demoted
  nor removed (409), and editors and viewers get 403. An owner who removes themselves
  is asked to confirm first, then taken back to the project list. The Team tab at
  `/projects/[id]/team` shows members with inline role changes, a removed list with
  Re-add, and an add-member panel with account search, role choice and role
  descriptions. Last-active times and member activity are not tracked yet, so they
  are not shown.
- Removing a project member now records who removed them (migration
  `AddMemberRemovedBy`), and the Team tab's removed list shows it, for example
  "Removed Sep 28 by Maya Chen · was Editor". Removals made before this change show
  no name, and re-adding a member clears it.
- Run results: `GET /api/projects/{id}/runs/{runId}` returns one run's number,
  status, timing, skipped scenarios, score, the previous comparable run (same
  environment and scoring version) and the request timelines of up to 20 scenarios
  that produced a failed or degraded finding, worst first. Another project's run
  returns 404. The page at `/projects/[id]/runs/[runId]`, opened from a run ID in the
  Test runs tab, shows the score with its change and evidence coverage, outcome
  counts, the category breakdown with the locked scoring version 1.0 weights, top
  findings, the failure timeline, recommendations and endpoint scores. Category and
  endpoint scores are computed in the browser from the run's findings (Pass 100,
  Degraded 50, Fail 0; empty categories are left out and their weight redistributed).
  Comparing runs, rerunning failed scenarios and PDF reports are not built yet, so
  their buttons stay disabled.

### Changed

- The shared 403 message now reads "You don't have permission to do this." instead of
  naming project settings, because every page shows it, the Team tab included.
- Run history evidence coverage now counts Degraded findings as scored.
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
- Migration `AddScenariosAndTargetSettings` replaces the free-text
  `DeveloperInvariant` on `ChaosScenarios` and `ScenarioExecutions` with a JSONB
  `Invariants` array, adds `ChaosScenarios.Reason` and a unique
  `(EndpointId, ScenarioType)` index, adds the environment label, timeout budget, and
  authorization confirmation to `ApiEnvironments`, and adds the `EnvironmentHeaders`
  table.

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
