# ChaosLab App Flow: Community / Self-Hosted Version

**Status:** updated 21 November 2026 to match PROJECT.md. Each process is tagged **[Done]** (implemented in the repository), **[Partial]**, or **[Planned]**. Items marked **[Open]** or **[Proposed]** are not decided yet; see PROJECT.md section 25.

Conventions used below:

- **Outcomes** are Pass, Degraded, Fail, Inconclusive, and Not Applicable, with severity Info, Low, Medium, High, Critical.
- **Execution is direct-first.** The runner sends requests to the target API. A supporting proxy or dependency simulator is used only when a fault cannot be produced from the request path alone.
- **Three kinds of failure** are always kept apart: the target API failed (a legitimate result), the ChaosLab runner failed (infrastructure error, never counted against the target), or the target was unreachable before the experiment (skipped with a reason, excluded from scoring).

---

## 1. End-to-end flows

### 1.1 Web flow (primary client)

Assumes the instance is already installed (see 2.0).

**First-run setup and sign-in**

1. Start the app and backend.
2. If the instance is uninitialized, the backend prints a one-hour setup link to the terminal. Open it (it points to `/install`, with the token in the URL fragment).
3. Enter name, username, email, and password to create the first SuperAdmin. The backend creates the account and role in one transaction and signs the user in.
4. On later visits, or if the instance is already initialized, show the login page. There are no default credentials and no public registration.
5. Authenticate with a username or email. The backend sets a secure session cookie.
6. If the account has a pending password change (for example an account an administrator created), redirect to the password-change page before anything else.

**Projects and setup**

7. Show the dashboard with the user's projects, recent runs, and calls to action.
8. Create a project (name, description). The creator becomes Owner.
9. Add project members and assign roles (Owner, Editor, Viewer).
10. Set the target API base URL and validate connectivity.
11. Import an OpenAPI specification (paste, upload, or URL).
12. Parse and validate the spec. Display discovered endpoints, methods, parameters, schemas, and security requirements.
13. Configure the target environment label (development, test, staging, mirror) and the target API credentials.
14. Optionally configure dependency simulation for faults that need it. **[Proposed]**
15. Validate connectivity and authentication against the target.
16. Include or exclude endpoints and mark operations that must not be tested.
17. Generate applicable scenarios and review them.
18. Optionally attach developer invariants to scenarios.

**Running a test**

19. Create a test run: choose endpoints, scenarios, and options, including concurrency (default one at a time, up to five).
20. Confirm warnings for state-changing operations and duplicate-request scenarios.
21. Start the run. Baseline requests run first where needed.
22. Execute scenarios, inject the configured failures, and observe the target.
23. Record requests, responses, timing, failures, and recovery information.
24. Evaluate behavior and produce findings (outcome, severity, category, evidence).
25. Calculate category scores and the overall Resilience Score.
26. Display results: score, evidence coverage, outcome counts, endpoint and scenario findings, failure timeline, recommendations.
27. Save the completed run and its results.
28. Optionally export a PDF report.
29. Return to the project dashboard.

**After a run**

30. The user can run another test, change API configuration, update the OpenAPI spec, review run history, manage members, or open reports.
31. Log out. The session is revoked and the user returns to the login page.

### 1.2 Mobile flow (read-only viewer, v1)

The mobile app is a **viewer only**. It cannot configure targets, import specifications, or start tests. The mobile stack is **[Open]**. Test-completion push notifications are not part of the v1 decision.

1. Start the mobile app and check for an authenticated session.
2. If none exists, show the login page and sign in with the backend's bearer-token endpoint. Store tokens securely on the device and refresh them as needed.
3. Show the mobile dashboard and the list of accessible projects.
4. Select a project to see its overview: project info, API configuration summary, latest Resilience Score, and recent test runs.
5. Select a test run to see status, Resilience Score, evidence coverage, scenario results, findings, failure timeline, detected resilience behavior, and recommendations.
6. Select a scenario or finding for details.
7. View available reports.
8. Log out. Tokens are revoked and the user returns to the login page.

---

## 2. Process details

### 2.0 Installation [Planned]

Decided (PROJECT.md 26, 21 November 2026). Not built: all of it. Build order: the `LICENSE` file, then the public site, then the release image, then the install script. The site launches before the image exists.

**Install paths**

- **Release image** (primary): one image holding the backend and the built frontend, published to GHCR for amd64 and arm64. A version tag (`v0.1.0`) publishes that version and moves `latest`. A `compose.yaml` at the repository root runs it with PostgreSQL under `docker compose` or `podman compose`.
- **From source**: clone the repository and run `make up` (`backend-pod.yaml`).

**Image installation**

1. Create `.env` with a generated `CREDENTIALS_KEY` (base64, 32 bytes) and `POSTGRES_PASSWORD`, by one documented command (a PowerShell variant for Windows) or by the install script. No secret is hardcoded.
2. Start the stack. The backend applies pending migrations at startup (`Database__MigrateOnStartup=true` in `compose.yaml`; off in development).
3. The backend serves the frontend as a single-page app on one origin (port 8080); every path outside `/api` falls back to `index.html`. The setup link's base URL defaults to `http://localhost:8080`.
4. Read the setup link from the backend's logs and continue with first-run setup (2.1).
5. Upgrading: pull the new image and restart; migrations apply on start.

**Install script** (`install.sh`, served from the public site)

1. Check for `docker` or `podman`, a compose plugin, and `openssl`; stop with a clear message if one is missing.
2. Create a `chaoslab/` folder and download `compose.yaml` from the script's own release tag.
3. Write `.env` only if none exists. An existing one is kept, because a new key or password would orphan the database and every stored credential.
4. Run `compose up -d`, wait for the setup link in the backend's logs, and print it.
5. No `sudo`, no telemetry. The site shows "read the script" and "install by hand" next to the command.

**Public site** (`Website/`, SvelteKit with `adapter-static`, deployed to GitHub Pages under `/ChaosLab`)

- Published only once the `LICENSE` (AGPL-3.0) is committed. No managed service is mentioned.
- Landing page, top to bottom: hero with the install block; the non-production warning directly under it; the pipeline (Import → Discover → Generate → Execute → Observe → Evaluate → Report); the chaos primitives; screenshots of a run's results and report; footer with license, GitHub, and docs links.
- Until `v0.1.0` exists, the install block shows the from-source steps with the note "One-command install arrives with v0.1.0. Until then, install from source."
- Docs pages: Installation, First-run setup, Configuration, Upgrading, Safe use (non-production targets only, authorization confirmation).
- Screenshots come from a run against a non-production target the team owns, checked for hostnames, emails, and credentials. A bundled demo API is a possible later improvement.

### 2.1 First-run setup [Done]

1. Start the backend against an uninitialized database.
2. The backend creates a random 64-character token held only in memory, valid for one hour, and prints a link ending in `/install#token=...`. The base URL comes from `BootstrapSetup:PublicUrl`, which must be an absolute HTTP(S) URL.
3. The `/install` page reads the token from the URL fragment, validates its format, and replaces the history entry so Back does not return to the secret link.
4. The user enters name, username, email, and password.
5. `POST /api/auth/setup` compares the token in constant time. A missing, invalid, or expired token returns 403.
6. The backend takes a PostgreSQL advisory lock, then creates the SuperAdmin role and account in one transaction. A second simultaneous request cannot also initialize the instance.
7. The new administrator is signed in immediately and is not forced to change the password just chosen.
8. After setup the endpoint returns 409. The `SuperAdmin` role is the permanent setup marker, so deleting or disabling the administrator never reopens first-run setup.
9. Restarting an uninitialized backend issues a new link and invalidates the old one. Initialized instances print no setup link. Terminal output and container logs that contain setup links must stay restricted.

### 2.2 Login [Done]

1. Show the login page.
2. The user enters a username or email and a password. An email shared by two accounts is rejected.
3. The backend validates credentials and counts failures toward lockout.
4. On success the backend sets an HttpOnly, Secure, SameSite=Lax cookie (web).
5. If `MustChangePassword` is set, redirect to the password-change page. Until the password is replaced, only `me`, `change-password`, and `logout` are reachable.
6. Otherwise open the dashboard.

### 2.3 Required password change [Done]

1. The user opens the password-change page (reached after login for accounts with a pending change).
2. Enter current password, new password, and confirmation.
3. The backend validates through Identity and updates the account.
4. The pending-change flag is cleared and the user continues to the dashboard.

### 2.4 Logout [Done]

1. The user selects Log out.
2. The client calls the logout endpoint.
3. Web: the cookie session ends. Mobile with a bearer token: the security stamp rotates, which revokes the user's refresh tokens on every device.
4. Local session and user-specific application state are cleared.
5. Redirect to the login page.

### 2.5 Dashboard [Done]

1. After sign-in, retrieve the projects the user belongs to (active memberships only).
2. Retrieve recent runs, score trend, current failed findings, and project summaries.
3. Display the dashboard: project count, endpoint count, recent run counts, average score with comparison to the previous period, score trend, recent runs, recent projects.
4. Calls to action, not finalized: create project, open project, configure API, import OpenAPI spec, start test run, view recent runs, view reports.
5. The selected action redirects to the matching process.

### 2.6 Project creation [Done]

1. The user selects Create Project.
2. Enter name (required, at most 200 characters) and optional description.
3. Validate. On failure, show validation errors.
4. Create the project and add the creator as Owner.
5. Show next steps: add members, configure the target API, or import an OpenAPI spec.

### 2.7 Project list and project detail [Done]

1. Retrieve the projects where the caller has an active membership, newest first.
2. Display the list. Selecting a project shows its detail: latest score, recent runs, API summary.

### 2.8 Project member management [Done]

Built as the project's Team tab. Only Owners add, remove, or change the role of members, and a project always keeps at least one Owner.

1. The user opens Project Members and sees current members and roles.
2. To add: choose a user and a role (Owner, Editor, Viewer). Validate, check the user is not already a member, create the membership, refresh the list.
3. Removing and re-adding a user changes the same membership record.
4. To remove: select a member, confirm, check the requester's permission, mark the membership removed, refresh the list.
5. The permission matrix (what each role may do) is **[Open]**.

### 2.9 API target configuration [Done]

1. Open the project and select API configuration.
2. Enter the base URL, environment label, and TLS verification setting. Validate the URL format.
3. Test reachability. If unreachable, show the failure and let the user edit the URL.
4. If reachable, show success and save the configuration.
5. Optional global headers and reusable test values can be saved here.
6. Production targets are not supported. The user must confirm that they own or are authorized to test the target.
7. Optionally enter a dependency URL: a service the target calls. Once saved, the page shows the simulator URL to put in the target's own configuration in its place (2.16).

### 2.10 OpenAPI specification import [Done]

1. Open API spec configuration.
2. Choose an import method: paste JSON or YAML, upload a file, or enter the URL of an OpenAPI document.
3. The backend receives the input and detects JSON or YAML.
4. Parse, verify the version is supported (OpenAPI 3.x; Swagger-compatible where convertible), and validate the structure.
5. On failure, return validation errors and let the user correct and re-import.
6. On success, extract metadata, servers, paths, methods, parameters, request and response schemas, status codes, and security schemes.
7. Save the specification and its parsed endpoint information.
8. Display the discovered endpoints for review, then continue to authentication configuration.

### 2.11 Endpoint discovery and selection [Done]

1. Read each path and each supported HTTP operation.
2. Extract request requirements, parameters, request bodies, and expected response schemas and status codes.
3. Create or update endpoint records linked to the project and spec.
4. Display discovered endpoints.
5. Let the user include or exclude endpoints from testing, and flag operations that must never be tested.

### 2.12 Target API authentication configuration [Done]

1. Read the security schemes declared in the specification.
2. If none is required, continue to validation.
3. Otherwise show the required scheme and let the user choose the type: Bearer token, API key, or Basic authentication.
4. The user supplies test credentials. They cannot be derived from the specification.
5. Store them encrypted, apart from non-secret configuration. They are never shown in reports or logs.
6. Send a validation request to the target. On failure, show the error and let the user update credentials. On success, mark the configuration validated.

### 2.13 Chaos scenario generation [Done]

1. The user selects Generate Scenarios after import and configuration.
2. Analyze each endpoint (method, request body, response types, status codes, security, idempotency expectations).
3. Decide which of the nine chaos primitives apply. Do not generate scenarios that do not apply.
4. Generate duplicate-request scenarios for suitable state-changing operations. These are behavioral scenarios, not a tenth primitive.
5. Assign default parameters, record why each scenario was generated, and save them.
6. Let the user enable or disable scenarios and adjust supported parameters.
7. The design target is 100+ applicable scenarios for sufficiently large APIs; small APIs are never padded.
8. Continue to invariants and test-run configuration.

### 2.14 Developer invariants [Done]

Built: invariants are saved on a scenario, snapshotted per execution, and checked by the evaluator (2.18). A read-back is a GET sent right after the scenario's requests; its path may use `{{request.body.<field>}}` templates filled from the request body that was sent. An invariant with a read-back is checked on the read-back's response, any other on every faulted request. The exception is Timeout / Drop: the runner ends the faulted request itself, so an invariant without a read-back has no response to check and is reported as not evaluated. The record count is the length of a top-level JSON array, or of the only array in a top-level object. An invariant that cannot be checked (no response, body not JSON, field missing) is reported as not evaluated and does not change the outcome.

1. On a scenario, the user chooses Add invariant.
2. Pick an assertion type and fill in its parameters. Examples: status code in a set, record count after duplicate requests at most N, a field never below a value, maximum duration, required header present. The final list of types is **[Open]**.
3. Optionally define a read-back request used to verify resulting state.
4. Save the invariant on the scenario. Each execution keeps a snapshot of it.

### 2.15 Test run configuration [Partial]

1. The user selects Create Test Run.
2. Retrieve the project, API configuration, endpoints, and scenarios.
3. Verify connectivity and the authentication configuration. If the target is unreachable, stop here; nothing runs.
4. Display the configuration.
5. Select endpoints and scenarios.
6. Set options, including concurrency: one experiment at a time by default, up to five if the user opts in.
7. Warn about state-changing methods and duplicate-request scenarios and require confirmation. Excluded operations are skipped.
8. Validate the configuration. If invalid, show the errors.
9. Create the run with the scoring version and configuration snapshot, set status to Queued, and continue to execution.

### 2.16 Resilience test execution [Partial]

Built: direct-request faults (Latency, Timeout / Drop, Duplicate Request) and baselines; a recovery request after each Timeout / Drop fault; the invariants' read-back requests; the fault injector (`Backend/Features/TestRuns/FaultInjector.cs`), which runs in process in the runner's request path; the dependency simulator (`Backend/Features/TestRuns/DependencySimulator.cs`), a backend route in the target's outbound path; the run's concurrency; and runner retries. Not built: the other faults in the dependency path.

**Concurrency** is one scenario at a time by default. With more, each endpoint is a lane whose scenarios run one after another in sequence order, and up to that many lanes run side by side, so scenarios on one endpoint never count each other's records. Dependency failures wait until every lane is done and then run one at a time, because the armed simulator cuts every call through the environment's route. Side by side, the target carries more load, so timings can read slower than they would alone.

**Runner retries** apply only to runner failures: an error inside the runner while it executes a scenario. The scenario is tried again after one second, up to the run's limit (0 to 3), unless the run was cancelled. A target failure, an unreachable target, and a request the spec can't fill in are never retried. Requests a failed try already sent stay on record: every observation carries `runnerAttempt`, and a retry numbers its requests after the stored ones. The evaluator judges the last try only.

The injector calls the target for real and changes what the runner receives. Each observation keeps the target's own status and body next to the injected ones (outcome `ModifiedResponse`):

- **5xx Storm**: `count` attempts in a row, each answered with the configured status at the configured probability, all sharing one idempotency key, then a request that gets through.
- **429 Throttle**: one request answered with 429 and `Retry-After`, a wait of that long (capped at the request timeout), then a normal request.
- **Malformed JSON** and **Empty Response**: one request whose body reaches the runner corrupted (truncated, invalid syntax, or wrong types) or empty.
- **Slow Drip**: one request whose body is read a few bytes per interval, then a normal request. The target only faces a slow reader when the body is larger than the socket buffers.
- **Authentication Expiry**: a request-side fault. The stored credential is left out (`missing`) or replaced with an invalid one (`invalid`, and `expired`, which cannot be minted from outside), then a request with the valid credential. `forbidden` is skipped with a reason, as is an operation with no stored credential.

In the runner's path, Malformed JSON, Empty Response, and 429 Throttle change only the runner's view (PROJECT.md 10.2); they show that the fault is reproduced, not how the target behaves.

**Dependency failure** uses the simulator instead. The target's dependency URL is pointed at `/api/simulator/{token}` on the ChaosLab backend, which forwards every call to the environment's dependency URL. The token is random, shown only to owners and editors, and replaced when the dependency URL changes or an owner or editor is removed or made a viewer. For the faulted request the runner arms the simulator, which then cuts the target's calls and logs them on the observation as `dependencyCalls`: `total`, and `calls` with the first 100 (time, method, path cut to 256 characters); a recovery request follows once it is disarmed. `unresolvable` closes the connection at once; `unreachable` stays silent until the target hangs up or the fault is disarmed. Neither is a real DNS failure or a refused connection: the target sees a closed connection. The scenario is skipped with a reason when the environment has no dependency URL, or when the target made no call through the simulator during the faulted request.

1. The user clicks Execute Tests. Set status to Running.
2. For each endpoint and scenario combination: build the request, apply credentials, and send a baseline request when needed.
3. Apply the configured failure. Most faults are applied through direct requests or the injector in the request path. Dependency faults use the simulator in the target's outbound path.
4. Observe the target's behavior and capture request, response, status, timing, errors, timeout information, repeated attempts, and recovery behavior.
5. Store the scenario execution result with a snapshot of its configuration.
6. Classify any problem as a target failure, a runner failure, or an unreachable target (skipped with a reason). Runner failures may be retried a limited number of times automatically.
7. When no scenarios remain, set the run status to Completed, PartiallyCompleted, or Failed.
8. Continue to evaluation.

### 2.17 Cancelling a run [Done]

1. The user selects Cancel on a queued or running test.
2. The runner stops starting new scenarios and lets in-flight requests finish or time out. It marks the scenario executions that never started as Skipped.
3. Set the run status to Cancelled and keep the evidence collected so far.
4. Partial runs are not scored as complete. **[Open]**: how a cancelled run is shown in history.

### 2.18 Resilience pattern evaluation [Partial]

Built (`Backend/Features/TestRuns/Evaluator.cs`): the runner evaluates each completed scenario into its findings, linked to the run, endpoint, and scenario execution, with evidence (expected, observed, per-request facts, interpretation, invariant results). Each finding stores its own scoring category. A Dependency failure yields up to three findings; every other fault yields one. The scenario's outcome is its worst finding, and a judged finding outranks an Inconclusive or Not Applicable one. Skipped and runner-failed scenarios get no finding. The direct-request faults have rules with **[Proposed]** thresholds:

- **Latency** and **Timeout / Drop** (Timeout): the delayed request, or the recovery request after a dropped or held one, is compared with the baseline. Answered normally is Pass; a body cut short, or slower than three times the baseline and by more than one second, is Degraded; a 5xx, no response, or a 4xx after a successful baseline is Fail; a failed baseline, or a 4xx with no baseline, is Inconclusive.
- **Duplicate Request** (Idempotency): any copy with a 5xx or no response is Fail; every copy rejected is Inconclusive. With the same key or no key, exactly one accepted copy is Pass, and several accepted copies are Inconclusive unless a held record-count invariant makes it Pass. With different keys, all accepted is Pass and some rejected is Degraded.

The injector's and the simulator's faults have rules with the thresholds decided in PROJECT.md 12.3. Each rule also sends a recovery request's result through the baseline comparison above: a recovery that fails makes the finding Fail, a slow one lowers a Pass to Degraded, and a baseline that did not succeed makes it Inconclusive.

- **Authentication Expiry** (Authentication Failure): 401 or 403 is Pass; a 3xx (redirects are not followed) or another 4xx is Degraded; a 5xx or no response is Fail; a 2xx is Fail with Critical severity.
- **Slow Drip** (Timeout): the target closing the connection during the slow read is Pass; a body of 64 KB or more delivered in full, or still arriving at the request timeout, is Degraded; a smaller body is Inconclusive; a response with no body is Not Applicable.
- **Dependency failure**, three findings:
  - Dependency Failure: an answer with a 2xx, 3xx, 502, 503, or 504 is Pass; any other status is Degraded; no response is Fail.
  - Retry, from the simulator's call log, per dependency method and path: more than 5 calls is Fail; 2 to 5 calls at least 100 ms apart is Pass; a gap under 100 ms is Degraded; one call is Inconclusive. The worst operation decides.
  - Graceful Degradation (pattern Fallback): a 2xx or 3xx while the dependency is down is Pass; a 502, 503, or 504 is Degraded; anything else is Fail.
  - When the baseline did not succeed, only the Dependency Failure finding is produced, as Inconclusive.
- **5xx Storm** (Retry), **429 Throttle** (Rate-Limit), **Malformed JSON** and **Empty Response** (Response Validation): only the runner sees these faults, so the finding is Fail when the target's own answer was a 5xx or missing on any request of the scenario, and Inconclusive otherwise. Empty Response on a body that is already empty, and Malformed JSON on a response that is not JSON, are Not Applicable.
- A violated invariant makes the finding Fail; for a Dependency failure that is the Dependency Failure finding. Fail is High severity, Degraded is Medium, everything else is Info.

Not built: circuit-breaker evidence, since no scenario sends the target several faulted requests in a row; Pass and Degraded rules for the faults only the runner sees, which need those faults in the dependency path.

1. Retrieve the observations for the run.
2. Evaluate behavior under each injected failure and each duplicate-request scenario.
3. Look for evidence of retry, backoff, timeout enforcement, rate-limit handling, circuit-breaker behavior, fallback and graceful degradation, authentication failure handling, response validation, dependency-failure handling, and idempotency.
4. Evaluate developer invariants. A violated invariant makes the finding Fail.
5. Compare expected behavior with observed behavior. Classify each finding as Pass, Degraded, Fail, Inconclusive (not enough observable evidence), or Not Applicable, with a severity.
6. Use cautious wording: state what was observed, what it suggests, and what is not proven. Never claim an implementation detail that cannot be observed.
7. Tag each finding with one scoring category, link it to the run, endpoint, and scenario execution, and attach evidence.
8. Send the findings to scoring.

### 2.19 Resilience Score calculation [Done]

Built: when a run ends with every scenario run (status Completed), the runner scores its findings and saves the overall score, evidence coverage, and category scores on the run, plus one endpoint score per endpoint that has scored findings. Partially completed, failed, and cancelled runs are not scored. Each finding is scored under the category stored on it; a finding saved before categories were stored takes the category of its scenario's fault. Rate-Limit and Response Validation can only score 0 or stay unscored until their faults run in the dependency path (2.18). Runs that finished before scoring existed are not backfilled. The run results page and run history show these stored numbers; a run without them is shown as not scored.

1. Retrieve the scored findings. Exclude Not Applicable and Inconclusive.
2. Convert outcomes to points: Pass 100, Degraded 50, Fail 0 (locked with scoring version 1.0).
3. A category score is the mean of its findings, so many easy scenarios in one category cannot dominate.
4. Apply the locked weights (scoring version 1.0): Timeout 20, Retry 15, Graceful Degradation 15, Rate-Limit 10, Idempotency 10, Authentication Failure 10, Response Validation 10, Dependency Failure 10.
5. Omit any category with no scored findings and redistribute its weight proportionally.
6. The overall score is the weighted mean of the remaining categories, from 0 to 100. Endpoint scores use the same method per endpoint.
7. Calculate evidence coverage: scored findings versus scored plus Inconclusive findings. Not Applicable findings are left out.
8. Save category scores, the overall score, evidence coverage, and the scoring version to the run, then send them to the dashboard.

### 2.20 Viewing test results [Partial]

Built: the run results page and API, showing the score, evidence coverage, category and endpoint breakdowns, findings, recommendations, and failure timelines of failing scenarios. Not built: there is no drill-down into a single endpoint or scenario. Runs executed before recommendations existed (2.22) show none.

1. The user opens a completed run.
2. Retrieve run information, scenario executions, findings, scores, and recommendations.
3. Display: overall score and evidence coverage, run status, category breakdown, counts of Pass / Degraded / Fail / Inconclusive / N/A, tested endpoints, executed scenarios, findings with severity, response and timing information, detected resilience behavior, failure timeline, and recommendations.
4. The user may open an individual endpoint or scenario to see detailed observations, then return to the run.

### 2.21 Failure timeline [Planned]

1. Retrieve the execution events of the run and order them chronologically.
2. Identify failure-injection start, requests sent, errors and responses received, repeated attempts, timeout events, recovery events, and scenario completion.
3. Build and display the timeline. The user can inspect individual events.

### 2.22 Recommendation generation [Partial]

Built: every Fail and Degraded finding is saved with one recommendation, in the same step that saves the finding. The evaluator names the problem behind the finding, because one scoring category holds different problems (a retry storm, retries without a pause, and a target that fails without any fault are all Retry findings), and a fixed rule per problem gives the title and the fix. The results page, the findings page, and the findings export show it. Not built: rules for honoring `Retry-After`, for validating malformed or empty responses, and for circuit breakers, because no finding can show those problems until their faults run in the dependency path (2.18) and circuit-breaker evidence is collected.

Decided: "weak" findings are not a separate kind; only Fail and Degraded findings get a recommendation, since an Inconclusive finding proves nothing about the API. A finding gets exactly one recommendation. Runs executed before this existed are not backfilled.

1. Take each Fail and Degraded finding as it is evaluated.
2. Identify the type of problem (the evaluator does this when it decides the outcome).
3. Map it to a deterministic, rule-based recommendation (for example bounded retries with exponential backoff and jitter, idempotency keys, explicit timeout budgets).
4. Attach the recommendation to the endpoint, scenario, finding, category, and severity.
5. Save, display in the results, and include in reports. ChaosLab does not change source code.

### 2.23 Test run history [Partial]

Built: the paged history with filters, date, status, score and its change from the previous comparable run, evidence coverage, and scenarios executed; each run opens its results. Not built: an endpoints-tested column, comparing two runs, and rerunning failed or degraded scenarios (both buttons are shown disabled).

1. The user opens Test History for a project.
2. Show previous runs with date, status, score, evidence coverage, endpoints tested, and scenarios executed.
3. Selecting a run opens its results.
4. A run can be compared with the previous one. Failed or degraded scenarios can be rerun manually.

### 2.24 PDF report generation [Done]

Built (`Backend/Features/Reports/`): owners and editors generate a report for any finished run (Completed, Partially completed, Failed, or Cancelled) from the run results page; every member can list and download a run's reports. Each generation stores a new version: the PDF goes to `Reports:Path` (a pod volume) and a `Reports` row keeps its file name and storage key. The content comes from the results page's own queries and the run's configuration snapshot, never from request headers, bodies, or credentials. The comparison with the previous run is its score change; side-by-side comparison is part of 2.23. An unscored run's report says why under limitations. The PDF is rendered with QuestPDF under its Community license, so an organisation that deploys ChaosLab and does not qualify for that license needs its own. Not built: deleting reports and retention rules.

1. The user opens a finished run and selects Generate Report.
2. Retrieve project and API information (sensitive values redacted), test configuration, results, scores, failure timeline, findings, recommendations, invariant results, limitations, and the comparison with the previous run when available.
3. Generate the PDF, store report metadata with a storage reference, and let the user view or download it.

### 2.25 Updating an OpenAPI specification [Done]

Built as Re-import on the specification page. A re-import saves a new spec version and retires the old one; there is no preview before saving, and re-importing the old document undoes it.

1. Open API specification settings and select Update / Re-import.
2. Provide the new document. Parse and validate it. A document identical to the active spec changes nothing.
3. Compare it with the stored spec and identify added, removed, and modified endpoints.
4. Update endpoint records. Testing choices carry over by path and method.
5. Decide which existing scenarios still apply and regenerate where needed: when the old spec had scenarios, the generation rules re-run on the new endpoints and edits carry over by path, method, and fault. Past runs keep their execution-time snapshots.
6. Save the new specification and display the updated structure.

### 2.26 Mobile login and viewing [Planned]

Mobile login

1. Open the app and check for a valid session. If one exists, open the dashboard.
2. Otherwise show the login page, send credentials to the backend, and show an error if they are invalid.
3. On success, receive bearer and refresh tokens, store them securely, load the user's permissions, and open the dashboard.

Mobile project viewing

1. Retrieve accessible projects and display them.
2. Selecting a project shows project info, API configuration summary, recent runs, and latest score.

Mobile test result viewing

1. Selecting a run requests its results from the backend.
2. Display status, score, evidence coverage, scenario results, failure timeline, findings, and recommendations.
3. Selecting a scenario or finding shows detail. The user can return to the run summary or the project.

### 2.27 Creating user accounts [Done]

Built as the workspace Team page (`/team`). Only the SuperAdmin creates accounts; there is no public registration. Editing, disabling, or deleting accounts is not built.

1. The SuperAdmin opens Team and enters first name, last name, username, email, and a temporary password.
2. Validate the fields. A username or email already in use is rejected.
3. Create the account with a pending password change and show a confirmation. The SuperAdmin shares the temporary password with the person.
4. At first sign-in the user must change the password (section 2.3) before anything else.
5. A project Owner can then add the account to a project (section 2.8).

---

## 3. What changed from the earlier version of this document

- Removed the default-SuperAdmin-credentials flow. Setup now uses the one-time setup link, and the user chooses their own credentials.
- Added a username to setup, and a separate required password-change flow.
- Execution is described as direct-first with an optional supporting injector, instead of always routing through a proxy.
- Replaced pass/fail language with the five outcomes and severity, and added the three failure kinds.
- Invariants are structured assertions, not free text.
- Scoring now follows the locked categories and rules, including evidence coverage.
- Added concurrency (default one, up to five), safety confirmations, run cancellation, and partial-run handling.
- Mobile is a read-only viewer. Member lists and test-completion notifications were removed from the mobile flow.
- Completed the empty "Initial Super Admin setup" and "Importing of OpenAPI Spec" sections and merged the duplicate import sections.
- Added status tags so the flow shows what exists today.
