# Changelog

All notable changes to ChaosLab are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). No versions have been
released yet, so history is grouped by date.

## [Unreleased]

### Added

- Faults in the target's dependency path. 5xx Storm, 429 Throttle, Malformed JSON and Empty
  Response got an "Injected in" setting on the scenario: the runner's request path, the
  default and the only path before, or the target's dependency path. In the dependency path
  the dependency simulator answered the target's first calls with the storm's status or a
  429 with `Retry-After`, or passed the calls on and corrupted or emptied the dependency's
  answer, and logged each call. The scenarios ran alone like Dependency failure and were
  skipped when no dependency URL was set or the target made no call through the
  simulator. They could now pass: a 5xx Storm was judged on the target's retries and its
  fallback, a 429 Throttle on waiting out `Retry-After`, and a broken answer on the target
  answering with a usable response or a controlled 502, 503 or 504 (PROJECT.md 12.3,
  decisions 7 to 9). Two recommendations came with them: honour `Retry-After` from a
  throttled dependency, and validate a dependency's response before using it. The
  scenario table marked faults in the dependency path. Scenarios saved before kept the
  runner's path. A dependency answer with no body (204, 205, 304, HEAD) or a compressed
  one was passed on unchanged, so the fault was Not Applicable to it. A 5xx Storm whose probability
  draws all missed was Not Applicable too.
- The AGPL-3.0 `LICENSE`. ChaosLab Community was licensed under the GNU Affero General
  Public License v3.0, and the website's deploy workflow could publish the site.
- Test values. Owners and editors saved values for an endpoint's required path, query and
  header parameters and its JSON request body under Test values on the specification
  page, and viewers saw them read-only. Runs sent a saved value ahead of the spec's
  example, default or enum value, so an endpoint whose spec had no examples, such as a
  FastAPI app's, could run. "Draft from schema" filled the body editor from the body's
  schema (required properties, enums, defaults and formats) for review; it was never sent
  until saved. Saved values carried over to the same path and method on re-import, and
  each run kept the values it was created with. The page said the values were not
  credentials. This settled PROJECT.md open decision 4.
- Import warnings for endpoints that couldn't run. The import report warned about each
  included endpoint whose required parameter or request body had no example and no saved
  test value, and the specification page marked those endpoints until values were saved.
  The import still succeeded.

- The container image served the web app. `make build` built the frontend with
  `adapter-static` and an `index.html` fallback and put it in the image, which served it
  on port 8080 next to the API, without sign-in. Every page path outside `/api` got the
  app's `index.html`, and `/api` paths never did. The image's first-run setup link
  pointed to `http://localhost:8080`, and the image was built from the repository root
  (`podman build -f Backend/Dockerfile .`). Local development with Vite was unchanged.
- Startup migrations. With `Database__MigrateOnStartup=true`, the backend applied pending
  database migrations when it started, before the run executor began, so an instance
  installed from the image needed no .NET SDK or `dotnet-ef`. The setting was off by
  default, and development kept using `make migrate`.

- Public website (`Website/`). A static SvelteKit site had a landing page and five docs
  pages: Installation, First-run setup, Configuration, Upgrading and Safe use. It showed the
  install-from-source steps and said the one-command install arrives with v0.1.0. A
  GitHub Actions workflow built it whenever `Website/`, the workflow or `LICENSE` changed on
  `develop`, and deployed it to GitHub Pages under `/ChaosLab`, but only once the repository
  had a `LICENSE` file.
- PDF reports. Owners and editors generated a report for any finished run from the run
  results page, and every project member could download it there. Each report had
  the run's status, environment, specification and configuration, the score, evidence
  coverage, outcome counts, category and endpoint scores, every finding with expected
  and observed behavior, evidence, invariant results and its recommendation, the
  failure timelines, the score change since the previous comparable run, and a
  warnings and limitations section that also said why an unscored run had no score.
  Generating again stored a new version and kept the old ones. Reports were built
  only from stored results and the run's configuration snapshot, so credentials and
  header values never appeared in them. The files were stored on a new
  `chaoslab-backend-reports` pod volume (`Reports:Path`), and `make clean-data`
  removed it along with the database. PDFs were rendered with QuestPDF under its
  Community license.
- Concurrency and runner retries for test runs. The run setup page let the user run up
  to five scenarios at once and retry runner errors up to three times, and the runner
  honored both settings instead of ignoring them. Each endpoint's scenarios still ran
  one after another, and Dependency failure scenarios ran alone after the others, so
  side-by-side scenarios could not skew each other's evidence. Only errors inside the
  runner were retried; target failures, unreachable targets, and requests the spec
  could not fill in were not. Requests sent by a failed try were kept, and every
  recorded request noted which try sent it.
- Rule-based recommendations. Every Fail and Degraded finding was saved with one
  recommendation that names a fix, such as limiting retries, waiting between them,
  storing idempotency keys, failing closed on a bad credential, or isolating a failing
  dependency. The rule was chosen by the problem the evaluator found, not by the
  finding's category, so a retry storm and a target that failed without any fault no
  longer shared the same advice. The results page, the findings page, and the findings
  export, which already had a place for recommendations, showed them. Runs executed
  earlier were not backfilled. Rules for honoring `Retry-After`, validating malformed
  or empty responses, and circuit breakers were left out, because no finding can show
  those problems yet.
- Evaluation rules for the injector's and the simulator's faults, so those scenarios
  produced findings instead of completing without one. Authentication Expiry was
  judged on whether the target rejected the request with 401 or 403, and a 2xx answer
  became a Critical finding. Slow Drip was judged on whether the target ended a slow
  read, and was Inconclusive for a body under 64 KB. A Dependency failure produced
  three findings: Dependency Failure, Retry (from the number and spacing of the calls
  the simulator cut), and Graceful Degradation. 5xx Storm, 429 Throttle, Malformed
  JSON, and Empty Response only change what the runner receives, so their findings
  were Fail when the target's own answer broke and Inconclusive otherwise. Not
  Applicable findings were produced for the first time, when a fault had nothing to
  act on. The thresholds were decided and recorded in `PROJECT.md` section 12.3; those
  of the direct-request faults stayed proposed. The rules are documented in
  `FeaturesFlow.md` section 2.18.
- Each finding stored its own scoring category, and the findings API returned it, so
  several findings from one scenario were scored and shown under different
  categories. Findings saved earlier kept the category of their scenario's fault.

- A dependency simulator, so Dependency failure scenarios ran instead of being
  skipped. A target environment gained an optional dependency URL, and the settings
  page showed owners and editors the simulator URL (`/api/simulator/{token}`) to put
  in the target's own configuration. The token was random, hidden from viewers, and
  replaced when the dependency URL changed or when an owner or editor was removed or
  made a viewer, so a link someone once saw stopped working. The simulator forwarded
  the target's calls to the dependency, and during the faulted request it cut them and
  logged them on the observation: the total number of calls, plus the first 100 with
  paths cut to 256 characters, so a target retrying in a tight loop could not grow
  the observation without bound. `unresolvable` closed the connection at once and `unreachable` stayed
  silent until the target gave up. The scenario was skipped with a reason when no
  dependency URL was set or when the target made no call through the simulator, and
  it completed without a finding because the evaluator has no rules for it yet. Because
  the route needs no sign-in, it dropped the `Cookie` header before forwarding, so a
  browser's ChaosLab session never reached the dependency, and it served the
  dependency's answers without `Set-Cookie` and with `Content-Security-Policy: sandbox`
  and `X-Content-Type-Options: nosniff`, so they could not run script or set cookies on
  ChaosLab's origin. The design decisions were recorded in `PROJECT.md` section 10.2.

- Resilience Score calculation: when every scenario of a run ran, the runner scored
  the run's findings and saved the overall score, evidence coverage, and the eight
  category scores on the run, plus a score for each endpoint with scored findings.
  Pass counted 100 points, Degraded 50, and Fail 0; a category score was the mean of
  its findings, and a category with no scored findings was left out with its weight
  shared among the rest. Partially completed, failed, and cancelled runs were not
  scored, and runs that finished earlier were not backfilled. Until the evaluator had
  rules for the injector's faults, only the Timeout and Idempotency categories could be
  scored. The points were locked as part of scoring version 1.0 in `PROJECT.md`
  section 14.3.

- A fault injector in the runner's request path, running inside the backend. 5xx
  Storm, 429 Throttle, Malformed JSON, Empty Response, Slow Drip, and Authentication
  Expiry scenarios ran instead of being skipped. The target was still called for
  real, and each observation kept the target's own status and body next to what the
  runner was made to see. Authentication Expiry left the stored credential out or
  replaced it with an invalid one; `expired` ran as an invalid credential, and
  `forbidden` and operations with no stored credential were skipped with a reason.
  These scenarios completed without a finding because the evaluator has no rules for
  them yet. Dependency failure stayed skipped until the dependency simulator exists.
- The run timeline showed the target's own status beside an injected one, and an
  injected error status no longer coloured the request as a target failure.
- Resilience evaluation: the runner turned each completed scenario into one finding
  with an outcome (Pass, Degraded, Fail, or Inconclusive), a severity, and evidence
  that separates what was observed, what it suggests, and what is not proven. Latency
  and Timeout / Drop scenarios were judged against the baseline request, and Duplicate
  Request scenarios by how many copies the target accepted. Faults that need the
  injector have no rules yet. The thresholds are proposed and documented in
  `FeaturesFlow.md` section 2.18.
- Developer invariants were checked during evaluation, and a violated invariant made
  the finding Fail. The runner sent each invariant's read-back request after the
  scenario, filling `{{request.body.<field>}}` templates from the request body. On a
  Timeout / Drop scenario an invariant without a read-back was reported as not
  evaluated, because the runner ends the faulted request itself. The finding and the
  scenario's outcome were saved in one transaction.
- A recovery request after every Timeout / Drop fault, so the finding rests on whether
  the target still answered after an abandoned request.
- An environment variable template, `Backend/.env.example`, for the local
  `Backend/.env.local` file, with placeholders for the pod database's connection
  string and the credentials key.
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
- Live run view: while a run is Queued or Running, `GET /api/projects/{id}/runs/{runId}`
  also returns `live`, with every started scenario execution (newest first, with its
  findings worst first) and the oldest execution still running, including its
  parameters, invariants and requests so far. The run page then shows progress with
  elapsed and estimated remaining time, Pass / Degraded / Fail / Inconclusive /
  Runner error / Skipped counts, an execution feed filterable to problems or runner
  errors, and a "Now running" panel. It reloads the run every 3 seconds and switches
  to the results view when the run finishes. A reload that fails keeps the last data
  on screen and is retried.
- Run cancellation: `POST /api/projects/{id}/runs/{runId}/cancel` marks a Queued or
  Running run Cancelled and keeps its evidence. Owners and editors can cancel; viewers
  get 403, another project's run 404, and a run that already finished 409. The status
  change is a single conditional update, so it never overwrites a run the runner
  finishes at the same moment. The runner re-checks the run status before starting
  each scenario.
- New test run page (`/projects/{id}/runs/new`), opened from the Run Test and New test
  run buttons, which only owners and editors now see. A preflight
  (`GET /api/projects/{id}/runs/preflight`) probes the default environment's base URL
  and shows the last credential validation of each security scheme the included
  operations use, the spec version and the scoring version. A missing spec, no
  included endpoints, a missing or unconfirmed target, or an unreachable target blocks
  the run; credential problems are only warnings. The page selects endpoints and
  scenarios (enabled defaults, or chosen per fault group or per scenario), concurrency
  1–5, baseline requests and 0–3 runner retries, and asks for confirmation when
  scenarios call POST, PUT, PATCH or DELETE or send duplicate requests.
  `POST /api/projects/{id}/runs` re-checks all of this, probes the target again, and
  creates a Queued run with scoring version 1.0, a configuration snapshot and one
  queued scenario execution per scenario with snapshots of its parameters and
  invariants. Viewers get 403.
- Run executor: a background service in the backend picks up Queued runs oldest first
  and runs their scenarios one at a time against the run's snapshotted target. Each
  request is built from the spec (path, required query and header parameters, and the
  JSON body, all from the spec's examples, defaults or enum values) with the
  environment's global headers and the stored credentials. Of the operation's security
  requirements, the first whose schemes all have a stored credential is sent in full,
  so an operation that needs two schemes together (a token and an API key) gets both;
  when none is complete the request goes unauthenticated. An API key in the query is
  added after the operation's own query parameters. The body is sent as the
  spec's own JSON media type. It sends a baseline request when the run asks for one,
  then applies the fault directly: Latency delays the
  request, Timeout / Drop holds the request open until the timeout or drops the
  connection after sending it, and Duplicate Request sends the configured copies with
  the same, different or no `Idempotency-Key`. Every request is stored as an
  observation with status, timing and the first 16 KB of the response (cookies are not
  kept, and a credential of 8 or more characters that the target echoes back is
  replaced with `[redacted]`), so the live view fills in as the run goes. A response
  the target cuts off mid-body, by closing or resetting the connection, or that the
  timeout interrupts, is stored with its status, an error, and the part of the body and
  byte count that arrived. A target that accepts the connection and closes it without
  a response is stored as the target's failure, with an error and no status. Requests
  without a body are sent with `Content-Length: 0`, so each one reaches the target
  exactly once. Global content headers such as `Content-Type` go on the request body.
  Faults that need the fault injector are skipped with a reason, a scenario whose
  requests can't connect to the target is skipped, and a request that can't be built
  (no example for a required value) is a runner error.
  Auth schemes come from the run's own spec, so a re-import while the run is queued
  doesn't change them, and a run whose environment moved to another host before it
  started fails without sending anything. The run re-checks for cancellation before
  each scenario, marks scenarios that never started as Skipped, and finishes
  Completed, PartiallyCompleted or Failed. Runs left Running by a backend restart or a
  failed runner poll are marked Failed. Concurrency and runner retries
  are not applied yet, and executions finish without an outcome until evaluation is
  built.
- Integration tests for the test-run API in `Backend.Tests`. They cover request
  validation and the stored configuration snapshot when a run is created, preflight
  blockers for a missing or unreachable target, which runs can be cancelled, viewer
  permissions, and the live run view's execution feed and current execution against
  a real PostgreSQL.
- SuperAdmin account creation. `POST /api/users` lets the SuperAdmin create an
  account with a username, first and last name, email, and temporary password; other
  signed-in users get `403` and a SuperAdmin with a pending password change is also
  refused. A username or email already in use is a validation error. The new user must
  change the temporary password at first sign-in, and can then be added to projects.
  The workspace Team page (`/team`) shows the create-account form to the SuperAdmin
  and points everyone else to each project's Team tab.

### Changed

- A required `Idempotency-Key` header no longer needed an example in the spec. The runner
  already sent its own key on every request, so it no longer failed those scenarios.
- When the runner couldn't fill a required parameter or request body, the failure reason
  said the value could be entered under Test values on the endpoint list. A required
  request body that wasn't JSON, such as a file upload, was reported instead as one
  ChaosLab couldn't send, on import, on the specification page and in the failure reason.
- The run results page said why scenarios failed. When scenarios failed, for example
  because the spec had no example for a required parameter, the runner note gave the
  number that failed instead of "No runner errors in this run." and listed the failed
  and skipped scenarios grouped by reason. The run history showed the failed count next
  to the scenario count. The run API returned the grouped reasons, and the PDF report
  used the same list.

- Re-importing an OpenAPI spec kept the project's scenarios. When the replaced spec
  had scenarios, the import re-applied the generation rules to the new endpoints in the
  same transaction, carrying edits over by path, method, and fault, so the project
  could run again without a separate Generate step. New endpoints got fresh scenarios,
  and removed endpoints kept theirs on the old spec for past runs. The import report
  gave the number of scenarios on the new spec, and the specification page showed it.
  A first import still generated nothing. Importing a document identical to the active
  spec returned `200` and saved nothing, instead of creating an empty new version.
- The backend logged one summary line per request, with its method, path, status
  code, and duration, and stopped logging every database command. The run executor
  checks for queued runs every two seconds, and its queries had filled the log even
  when nothing was running. Query strings were left out of the request line, since
  they can carry tokens, and dependency simulator calls were logged without their
  path, which holds the environment's simulator token.

- A scenario's whole-number settings were refused when written as `3000.0` or `3e3`.
  They used to be saved, and the runner then failed on every run of that scenario with
  an internal error.
- An invariant's read-back request no longer carried the header parameters of the
  endpoint under test, such as a fresh idempotency key. It still sent the
  environment's headers and the credentials.
- The run results page showed the category breakdown, endpoint scores, and evidence
  coverage that the backend stored when it scored the run, and the browser's own copy
  of the scoring method was removed. A run without a stored score (partially
  completed, failed, cancelled, or still running) was shown as not scored instead of
  getting a breakdown worked out from its findings, and run history showed no evidence
  coverage for it. Run history rounded a coverage ending in .5 up, so it matched the
  results page. PROJECT.md section 14.3 recorded the rule.

- A scenario's outcome became its worst finding, with a judged finding ranked above an
  Inconclusive one, and the live run feed listed a scenario's findings in the same
  order. A scenario could now end as Not Applicable.

- The project page's resilience breakdown showed the eight locked scoring categories
  from the latest scored run's stored category scores. It previously listed six
  older labels, including Availability, Circuit breaker, and Fallback behavior, that
  nothing filled in.
- Account emails are now unique, case-insensitively, enforced by a unique database
  index. Two account creations racing on the same email or username now get a `400`
  validation error instead of both succeeding or failing with a `500`. Login by email no
  longer needs to guard against an email shared by two accounts. The migration fails on
  a database that already holds two accounts with the same email; resolve those first.
- Requests to targets and spec URLs no longer keep or send back cookies. The backend
  shares its HTTP connections across projects and runs, so a session cookie one target
  set could otherwise ride along on another project's requests to the same host. An
  API key configured as a cookie is still sent.
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
- Retagged `FeaturesFlow.md` against the shipped code: project member management,
  OpenAPI specification import, and endpoint discovery and selection are now Done;
  viewing test results and test run history are now Partial, with notes on what is
  not built yet.
- Migration `AddScenariosAndTargetSettings` replaces the free-text
  `DeveloperInvariant` on `ChaosScenarios` and `ScenarioExecutions` with a JSONB
  `Invariants` array, adds `ChaosScenarios.Reason` and a unique
  `(EndpointId, ScenarioType)` index, adds the environment label, timeout budget, and
  authorization confirmation to `ApiEnvironments`, and adds the `EnvironmentHeaders`
  table.
- The development pod in `backend-pod.yaml` now publishes PostgreSQL (5432) and the
  backend (8080) on `127.0.0.1` only. They were previously reachable from any host on
  the same network, which exposed the database with the example password.
- Adding a project member or changing a member's role now requires `role` and returns
  `400` when it is missing. Previously a missing role silently bound to Owner.
- The run preflight's reachability probe, also repeated when a run is created, now
  waits at most 30 seconds, like the Settings connection check. It used the
  environment's request timeout, up to 300 seconds, so a target that never answered
  left the New Run page waiting for minutes.
- The run executor now sends a new `Idempotency-Key` value on every request instead of
  the spec's example value. With the fixed example, the baseline and fault requests of
  every run shared one key, so a target that honors the header replayed its stored
  answer and the fault was never really executed.
- The development pod now keeps the backend's ASP.NET Data Protection keys in the
  `chaoslab-backend-keys` volume. They lived inside the container, so every
  `make restart` invalidated all login cookies and mobile refresh tokens.
- The README's Podman steps now include applying the database migrations, which a
  fresh database needs before the backend can start, and document `Backend/.env.local`
  (the file the backend and `make migrate` actually read) instead of `Backend/.env`.
- Web sessions in other browsers now end within a minute of a password change. The
  security stamp behind the login cookie was rechecked only every 30 minutes, so a
  stolen cookie kept working for up to half an hour after the password was changed.
- The New Run page no longer offers "Concurrent experiments" or "Automatic retries for
  runner errors", because the runner always runs one experiment at a time and never
  retries. The run API now defaults `runnerRetries` to 0 instead of 2, so a run's saved
  configuration matches what the runner does.
- A run and credential validation now apply an environment's global headers through
  one shared helper, so the two can no longer drift apart. A run that cannot decrypt a
  secret header now also names the likely cause, a changed `Credentials:Key`.

### Removed

- The workspace sidebar's Test Runs, Findings, and Settings links, and the Overview
  page's "Run test", "View all runs", and open findings "View all" links. They led to
  pages that do not exist yet and showed a not-found error. Runs and findings are
  still reached from each project.
- The SvelteKit template leftovers: the public `/demo` and `/demo/playwright` pages and
  their end-to-end test, the `src/lib/vitest-examples` sample, and the empty
  `src/lib/index.ts`.
- The unused `Backend/compose.yaml` (the dev stack runs from `backend-pod.yaml`) and its
  entry in `Backend.sln`, and the unused `User.CreatePendingUsn()` helper.
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
