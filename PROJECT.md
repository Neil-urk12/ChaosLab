# ChaosLab

> Automated API Resilience, Failure Testing, and Assurance Platform

**Document status:** updated 8 November 2026. This version merges the earlier PROJECT.md, the ChaosLab Context Export, and the current state of the repository. Where they disagreed, the decisions in section 26 apply. Items still undecided are collected in section 25 and marked **[Open]** or **[Proposed]** where they appear.

---

# 1. Project Overview

**ChaosLab** is a developer-facing resilience testing and assurance platform that evaluates how RESTful APIs and their host applications behave under controlled failure conditions before deployment.

The platform imports an **OpenAPI 3.x / Swagger specification**, discovers endpoints and their requirements, generates applicable resilience-testing scenarios, executes them against a running API in a controlled non-production environment, observes the resulting behavior, and produces evidence-backed findings, an explainable Resilience Score, recommendations, and reports.

ChaosLab is primarily intended for:

- Backend Developers
- DevOps Engineers
- DevSecOps Engineers
- Site Reliability Engineers
- Infrastructure Engineers
- Software QA / Test Engineers

DevSecOps users are included because they own pre-deployment quality gates. ChaosLab is **not** a security scanner (see section 17): it evaluates failure handling, not vulnerabilities.

The project is developed as an academic capstone (ACLC College of Mandaue, BS Computer Science), while keeping the architecture suitable for later growth into an open-source tool and possibly a hosted platform.

**Naming:** "ChaosLab" and "ChaosAPI Lab" refer to the same project. Use **ChaosLab** in formal documents. The latest working title is *ChaosLab: Automated API Resilience and Failure Testing and Assurance Platform*. "Assurance" means an evidence-backed assessment, not a guarantee or certification.

---

# 2. Problem

Traditional API testing verifies that an API behaves correctly when everything works:

- Does `POST /orders` create an order?
- Does `GET /users/{id}` return the expected schema?
- Does invalid input return the right validation error?
- Does authentication protect an endpoint?

These tests do not answer resilience questions such as:

- What happens when a dependency becomes slow?
- Does the application retry failed requests, and are retries bounded?
- Does the client respect `429 Too Many Requests` and `Retry-After`?
- Does a timeout actually terminate the operation?
- What happens when a service returns malformed or empty JSON?
- Does the application fall back gracefully?
- Can duplicate requests create duplicate records?
- Does a circuit breaker activate during repeated failures?
- Does the system recover after a temporary outage?

Existing chaos-engineering tools can be powerful, but many target infrastructure or Kubernetes, need specialist setup, or require every experiment to be configured by hand. ChaosLab focuses on an accessible, OpenAPI-driven workflow for REST APIs.

---

# 3. Core Concept

```text
OpenAPI Specification
        │
        ▼
API Discovery
        │
        ▼
Scenario Generation
        │
        ▼
Controlled Execution against the Target API / Application
(direct requests; supporting proxy or simulator where a fault needs it)
        │
        ▼
Observation & Evidence Collection
        │
        ▼
Resilience Evaluation (built-in rules + developer invariants)
        │
        ▼
Findings, Resilience Score, Recommendations, Report
```

ChaosLab does **not** test an OpenAPI document by itself. The document describes the API; resilience testing requires a **running target API/application**.

---

# 4. Version 1 Goals

V1 must prove this workflow end to end:

1. Create a project.
2. Import an OpenAPI specification.
3. Parse endpoints, parameters, schemas, and authentication requirements.
4. Configure the target environment and test credentials.
5. Validate connectivity and run baseline requests.
6. Generate applicable resilience scenarios.
7. Select scenarios and endpoints to test.
8. Execute controlled experiments within the safety limits.
9. Capture observations and evidence.
10. Evaluate behavior with built-in rules and developer invariants.
11. Classify results (Pass, Degraded, Fail, Inconclusive, N/A) with severity.
12. Calculate the explainable Resilience Score.
13. Display endpoint-level and scenario-level findings and recommendations.
14. Export a PDF report and keep basic run history for comparison.

The capstone prioritizes a **working end-to-end pipeline** with a limited, defensible set of scenarios over breadth of features.

---

# 5. Scope

## 5.1 OpenAPI Import

- OpenAPI 3.x JSON and YAML; Swagger-compatible specifications where reasonably convertible.
- Source can be pasted, uploaded, or fetched from a URL (the data model already records `Paste`, `FileUpload`, `Url`).
- Discovered: API metadata, servers, paths, HTTP methods, parameters, request bodies, response schemas, security schemes (global and per operation), tags, operation IDs.
- The specification plans scenarios; it never replaces a running target.

## 5.2 Target API Testing

Tests run against real REST APIs in controlled environments: local development, development servers, QA, staging, containerized test environments, or isolated production-mirror environments. **Production fault injection is out of scope.**

## 5.3 Clients

- **Web application (primary):** all functionality.
- **Mobile viewer (v1, read-only):** sign in, browse accessible projects, view project overview, recent test runs, run results (status, score, scenario results, failure timeline, detected behavior), recommendations, and available reports. It cannot configure targets, import specifications, or start tests. The mobile stack is **[Open]**. The backend already issues bearer and refresh tokens for this client.

---

# 6. Chaos Injection Primitives

ChaosLab V1 defines **exactly nine** chaos primitives.

## 6.1 Latency
Delay a response or request path by a configurable amount (for example +3000 ms on a normal 120 ms response). Evaluates timeout configuration, retry behavior, user-facing degradation, slow-dependency handling.

## 6.2 5xx Storm
Return intermittent or repeated HTTP 500, 502, or 503 responses at a configured probability or duration. Evaluates retry handling and limits, backoff, circuit breaking, fallback.

## 6.3 429 Throttle
Return `429 Too Many Requests`, optionally with `Retry-After`. Evaluates rate-limit handling, retry timing, backoff.

## 6.4 Timeout / Drop
Hold, drop, or terminate a response or connection. Evaluates timeout enforcement, retry behavior, error propagation, resource cleanup.

## 6.5 Malformed JSON
Return syntactically invalid or structurally corrupted JSON. Evaluates parser failure handling, schema assumptions, error boundaries.

## 6.6 Empty Response
Return an unexpectedly empty body where a payload is normally expected. Evaluates null and missing-payload handling.

## 6.7 Slow Drip
Deliver response content very slowly instead of delaying the whole response. Exposes systems that enforce connection timeouts but not complete-operation deadlines.

## 6.8 Authentication Expiry
Simulate missing, expired, invalid, or unauthorized credentials using 401 and 403 responses. Evaluates token refresh, reauthentication, credential-failure handling, authorization-error handling.

## 6.9 DNS / Dependency-Resolution Failure
Simulate an unresolvable or unreachable dependency. In V1 this is a **simulation of dependency-resolution or reachability failure**, not manipulation of real DNS infrastructure.

A **baseline execution** is part of the workflow but is not a chaos primitive.

---

# 7. Duplicate Request Testing

Duplicate-request testing is a **behavioral scenario** generated by the scenario engine. It is **not** a tenth primitive.

It sends semantically equivalent requests more than once, potentially with the same idempotency key, for example two identical `POST /payments`. Expected: one logical record. Failure: multiple records or transactions from one logical operation.

Candidates are state-changing operations (POST, PUT, PATCH), subject to the safety rules in section 16 and to the availability of cleanup or controlled test data. Duplicate requests are the only deliberate concurrency-like behavior in V1; ChaosLab does not do general race-condition testing.

---

# 8. Scenario Generation

ChaosLab does not blindly apply every failure to every endpoint. Applicability is decided from endpoint characteristics:

- HTTP method
- Request body and schemas
- Expected response types and status codes
- Whether the endpoint returns a body
- Parameters and headers
- Declared security requirements
- Endpoint semantics and idempotency expectations
- Capabilities of the fault injector

Conceptual model:

```text
Scenario = Endpoint × Applicable Fault Type × Fault Parameters
           × Authentication State × Expected Resilience Behavior
```

Each generated scenario records **why** it was generated and what it intends to evaluate. Example: `GET /users` can receive Latency, 5xx, 429, Timeout, Malformed, and Empty scenarios; `POST /payments` additionally receives Duplicate Request.

**Scenario-count target:** for sufficiently large specifications ChaosLab should be capable of producing **100+ applicable scenarios**. This is a design target, not a guarantee. Small APIs are never padded with irrelevant scenarios.

Scenario defaults (`IsDefaultEnabled`) are suggestions for future runs. A scenario execution keeps a snapshot of the configuration used, so history stays valid if scenarios are later edited or regenerated.

---

# 9. Authentication

## 9.0 Target API authentication

Supported for the target API: Bearer token, API key, Basic authentication, and none where applicable.

OpenAPI can describe how authentication works but contains no valid credentials, token-issuance flows, or custom login procedures. The user supplies test credentials separately. Credentials are stored encrypted, kept apart from non-secret profile configuration, and **never appear in exported reports or logs**.

## 9.1 ChaosLab instance initialization

Instance authentication is separate from target API credentials. The first SuperAdmin is created through a PocketBase-style first-run setup link, not a pre-created account with a default or environment-supplied password.

- Before initialization, backend startup prints a secret, random, one-hour link to the frontend `/install` page. The token is carried in the URL **fragment**, not published through an anonymous credentials endpoint.
- The operator supplies name, username, email, and password. The backend requires the token, validates through ASP.NET Core Identity, and creates the SuperAdmin and role atomically before signing in with the web cookie. The chosen password does not require an immediate change.
- The `SuperAdmin` role records completed initialization. Transaction-level PostgreSQL locking prevents simultaneous setup requests from creating multiple administrators. Completed setup cannot be replayed or reopened by deleting or disabling the administrator.
- Restarting an uninitialized backend replaces the in-memory token. Initialized instances preserve accounts and print no setup link. Terminal output and container logs containing setup links must remain restricted.
- Public registration is disabled. Required password changes for accounts created by administrators are a separate flow.

## 9.2 Sessions

- Web clients: HttpOnly, Secure, SameSite=Lax cookie from `POST /api/auth/login`.
- Mobile clients: Identity bearer tokens (15-minute access token) with refresh.
- Every endpoint requires sign-in by default. An account with `MustChangePassword` can reach only `me`, `change-password`, and `logout`.
- Failed logins count toward lockout. Login accepts a username or an email.

## 9.3 Roles

System role: `SuperAdmin`. Project roles (`Owner`, `Editor`, `Viewer`) are separate from system roles; project membership is a per-project relationship, and removing then re-adding a member changes the same membership.

---

# 10. Fault Injection Architecture

## 10.1 Baseline: direct-first with a supporting proxy

The primary V1 model is **direct API resilience testing**: the runner sends scenario requests to the configured target API and evaluates what it observes. A proxy or dependency simulator is an **internal supporting mechanism** used when a fault cannot be produced from the incoming path alone. It is not a separate user-facing mode in V1.

```text
Primary:       Runner ───────────────────────────► Target API
Supporting:    Runner ──► Fault injector (proxy) ──► Target API
Dependency:    Target API ──► Fault injector / simulator ──► (stands in for a downstream dependency)
```

ChaosLab evaluates how the **target application/API** handles the simulated failure. It does not certify or repair an external dependency.

## 10.2 What is under test, and where the injector sits

**[Proposed — validate with a proof of concept before promising any row as fully supported.]**

| Primitive | Primary scoring category | What is under test | Injection point | Feasibility |
|---|---|---|---|---|
| Latency | Timeout | Timeout budgets of the target and its callers | Runner-side delay, or injector delaying the response | Straightforward |
| 5xx Storm | Retry (and Graceful Degradation) | Retry limits, backoff, fallback of the target | Dependency path (target's outbound calls); synthetic 5xx to the runner only tests the runner | Straightforward for HTTP; dependency path needs the simulator |
| 429 Throttle | Rate-Limit | Handling of `429` and `Retry-After` | Dependency path, or synthetic 429 | Straightforward |
| Timeout / Drop | Timeout | Timeout enforcement and cleanup | Injector holding or dropping the connection | Needs proof of concept |
| Malformed JSON | Response Validation | Parsing and error boundaries | Injector modifying or synthesizing the body | Straightforward |
| Empty Response | Response Validation | Null and missing-payload handling | Injector returning an empty body | Straightforward |
| Slow Drip | Timeout | Complete-operation deadlines | Injector streaming slowly | Needs proof of concept |
| Auth Expiry | Authentication Failure | Behavior on invalid, expired, or unauthorized credentials | Runner alters credentials, or synthetic 401/403 | Straightforward |
| DNS / Dependency-Resolution | Dependency Failure | Dependency-failure handling | Simulator that is unresolvable or unreachable | Needs proof of concept |
| Duplicate Request (behavioral) | Idempotency | Side-effect deduplication | Runner sends duplicates directly | Straightforward; needs safe data and cleanup |

Rules that follow from the table:

- If ChaosLab only injects a synthetic response into the **runner's** request path, it observes the runner's view. It cannot by itself prove how a separate consuming application retries or opens a circuit breaker.
- Testing the target's resilience to a failing **downstream dependency** requires the simulator to sit in the target's outbound path. That requires target configuration (for example pointing its dependency URL at the simulator).
- Retry, backoff, circuit-breaker, and fallback behavior may live inside the application. Without suitable placement, logs, traces, or hooks the result is **Inconclusive**, never guessed.
- YARP is the proposed starting point for HTTP-level injection. It should not be assumed to implement connection drops, resets, partial delivery, slow-drip, or DNS failure on its own; custom connection handling or a Toxiproxy-like mechanism may be required.

## 10.3 Three kinds of failure

ChaosLab keeps these distinct everywhere (status, data model, reports, score):

1. **Target API failed** during an experiment. This is a legitimate result.
2. **ChaosLab runner/injector failed.** This is an infrastructure error, never counted against the target. A limited automatic retry is allowed.
3. **Target unreachable before the experiment.** The test never executed; it is skipped with a reason and excluded from scoring.

---

# 11. Resilience Patterns and Observation Philosophy

## 11.1 Patterns evaluated

- **Retry:** whether failed requests appear to be retried; retry count, interval, immediate retry loops, recovery.
- **Backoff:** fixed versus increasing delay, retry-storm behavior, jitter where observable.
- **Timeout enforcement:** whether an operation ends within an expected time.
- **Rate-limit handling:** respect for `Retry-After`, bounded backoff.
- **Circuit breaker:** behavior consistent with temporarily stopping calls after repeated failures.
- **Fallback and graceful degradation:** whether an alternative or reduced response is provided.
- **Authentication failure handling:** failing closed, consistent errors, no protected data exposed.
- **Response validation:** handling of malformed or empty bodies.
- **Dependency-failure handling:** isolation and controlled degraded responses.
- **Idempotency:** whether repeated identical requests cause duplicate side effects, attempted only where safe and where cleanup or controlled data exists.

## 11.2 Observation philosophy

ChaosLab evaluates **observable behavior** and keeps evidence and interpretation separate:

```text
Observed:    Three requests occurred after the injected failure.
Inference:   The behavior is consistent with retry logic.
Not proven:  The application uses Polly retry middleware.
```

Reports use wording such as "retry-like behavior observed", "no recovery observed", "timeout occurred after approximately X seconds", "repeated requests were detected". They never claim implementation details that cannot be directly observed. When evidence is insufficient the result is **Inconclusive**.

---

# 12. Result Model

## 12.1 Outcomes

| Outcome | Meaning |
|---|---|
| **Pass** | Expected behavior for the tested condition; no rule or invariant violated. |
| **Degraded** | Still functioning but partially unacceptable (for example excessive latency, suboptimal retries, incomplete fallback, recoverable inconsistency). |
| **Fail** | Violates expected behavior, violates an invariant, or does not recover within configured limits. |
| **Inconclusive** | Not enough observable evidence to decide. |
| **Not Applicable** | The scenario does not logically apply to the endpoint. |

Runner failures and unreachable targets (section 10.3) are **not** outcomes about the target.

## 12.2 Severity

`Info`, `Low`, `Medium`, `High`, `Critical`, attached to findings and recommendations.

## 12.3 Thresholds

Exact thresholds that separate Pass, Degraded, and Fail per pattern (for example how long a response may take before Latency becomes Degraded) are **[Open]**. They must be fixed and documented before formal evaluation.

---

# 13. Developer-Defined Invariants

Built-in rules cannot infer business-specific correctness. V1 supports **simple, structured invariants** attached to a scenario.

- An invariant is a small JSON assertion (`type` plus `params`) configured in the UI, not free text and not an expression language.
- Invariants are evaluated by the evaluator against recorded observations. An optional read-back (verification) request can check resulting state.
- A violated invariant makes the finding **Fail**. Results are stored with evidence and appear in the report.
- Examples: a duplicate order request creates at most one order; repeating the same payment idempotency key yields at most one transaction; inventory never goes negative; the status code is one of an allowed set.
- **[Proposed initial assertion types]:** status code in a set; response field equals / not below a value; record count after duplicate requests at most N; maximum duration in milliseconds; required header present. The final list is **[Open]**.

---

# 14. Resilience Score

## 14.1 Range and principles

Overall **Resilience Score: 0–100**, deterministic, explainable, and traceable to findings, evidence, categories, and weights. It is comparable across runs under defined, repeatable conditions. It is **not** a reliability certification, and passing all executed scenarios does not prove the API has no resilience defects.

## 14.2 Categories and weights (locked, scoring version 1.0)

| Category | Weight |
|---|---:|
| Timeout Handling | 20% |
| Retry Behavior | 15% |
| Graceful Degradation | 15% |
| Rate-Limit Handling | 10% |
| Idempotency | 10% |
| Authentication Failure Handling | 10% |
| Response Validation | 10% |
| Dependency-Failure Handling | 10% |
| **Total** | **100%** |

The weights are fixed and recorded before evaluation as scoring version `1.0` (stored on every run). Circuit-breaker and fallback behavior are evidence signals feeding the categories above, not separately scored categories. Earlier weight sets (the original proposal, the reconciled-requirements example, and the prototype) are superseded.

## 14.3 How the score is computed

1. Each **finding** is tagged with one category. A scenario can produce several findings (for example a 5xx Storm can yield a Retry finding and a Graceful Degradation finding).
2. Findings are scored by outcome. **[Proposed]** Pass = 100, Degraded = 50, Fail = 0.
3. A **category score** is the mean of its scored findings, so many easy scenarios in one category cannot dominate the overall score. Category weights, not scenario counts, set influence.
4. **Not Applicable** and **Inconclusive** findings are excluded from scoring.
5. A category with no scored findings is omitted and its weight is redistributed proportionally across the remaining categories.
6. The **overall score** is the weighted mean of the scored categories.
7. Every report shows **evidence coverage** (scored findings versus total, including Inconclusive) so a high score from thin evidence is visible.
8. Endpoint scores use the same method restricted to one endpoint.

How baselines affect evaluation, and whether severity caps the score (for example a Critical failure limiting the maximum), are **[Open]**.

---

# 15. Findings, Recommendations, and Reports

## 15.1 Findings

Each finding records: endpoint and method, scenario or fault type, outcome, severity, category and pattern, expected behavior, observed behavior, supporting evidence (attempt and correlation IDs, status codes, timestamps, timing), and the rule-based recommendation.

Example: `POST /orders`, Duplicate Request, **Fail**, **High**. Expected one logical order for the same idempotency key; observed two orders; recommendation: implement server-side idempotency-key validation and persistence.

Raw observations stay separate from derived findings, scores, and recommendations.

## 15.2 Recommendations

Deterministic and rule-based. ChaosLab does not patch source code.

- Immediate repeated retries: bounded retry limits, exponential backoff, jitter.
- 429: respect `Retry-After`, bounded backoff.
- Duplicate side effects: idempotency keys or transactional uniqueness.
- Malformed or empty responses: validate content type and schema, handle explicitly.
- Timeouts: connect, read, and overall timeout budgets with bounded fallback.
- Dependency failures: isolate failures, return controlled degraded responses.
- Authentication failures: fail closed, consistent errors, no protected data.
- Circuit-breaker weaknesses: consider observable circuit-breaker policies.

## 15.3 Dashboard

Run summary (score, total, passed, degraded, failed, inconclusive, N/A or skipped, evidence coverage), category breakdown, finding list, failure timeline, score trend across runs, and recent runs.

## 15.4 Reports

Exportable as **PDF**. Contents: project and target environment (sensitive values redacted), execution details, OpenAPI metadata and version, test configuration, overall and category scores, outcome counts, endpoint-level results, scenarios, expected versus observed behavior with evidence, severity, recommendations, invariant results, failure timeline, comparison with the previous run when available, and warnings and limitations.

## 15.5 History and retesting

Basic run history is in V1 for comparison. Failed or degraded scenarios can be rerun manually. Scheduled retesting is future work.

---

# 16. Safety and Execution Limits

ChaosLab is for **controlled, authorized testing** only:

- APIs the tester owns or has explicit authorization to test.
- Development, QA, staging, dedicated test, or isolated production-mirror environments.
- No uncontrolled disruption of third-party or production infrastructure.

Controls:

- Users are responsible for safe test data and non-production credentials.
- State-changing methods (POST, PUT, PATCH, DELETE) and duplicate-request scenarios require warnings and per-operation exclusion controls.
- **Concurrency:** experiments run **one at a time by default**, so timing measurements stay clean. A user may raise this per run, up to a hard maximum of **five** active experiments. The setting is stored in the run configuration.
- Distinguish target failure, runner failure, and unreachable target (section 10.3).

---

# 17. V1 Limitations and Non-Goals

**Limitations**

1. REST only. No GraphQL, gRPC, WebSockets, or SOAP.
2. Non-production only.
3. No load or stress testing (not comparable to k6 or JMeter).
4. No general race-condition or concurrency testing. Duplicate requests exist only for idempotency evaluation.
5. No security vulnerability scanning, penetration testing, or OWASP-style assessment.
6. No automatic repair of source code.
7. Credentials, token flows, and custom login behavior must be configured by the user.
8. DNS failure is simulated.
9. Internal retry, circuit-breaker, and fallback behavior may be Inconclusive without observable evidence.
10. Business correctness is only checked through developer-defined invariants.
11. Concurrency capped at five simultaneous experiments.
12. CI/CD gating, build thresholds, and pipeline failure rules are deferred.
13. Hosted SaaS, multi-organization workspaces, billing, advanced collaboration, and enterprise administration are deferred.
14. The mobile client is a read-only viewer.

**Non-goals** (unless explicitly promoted): Kubernetes-native chaos, production-wide infrastructure chaos, CPU or memory exhaustion, disk corruption, node or pod termination, cluster network partitioning, browser UI testing, testing of mobile applications, complete distributed tracing, full APM, source-code static analysis, replacing Playwright, Postman, or unit and integration test frameworks.

**Future phases:** more protocols; an explicit advanced dependency-proxy mode; selected concurrency testing; richer tracing; CI/CD integration with score gates; scheduled tests; hosted execution; public signup, organizations, RBAC, billing; enterprise governance, audit, SSO.

---

# 18. Technology Stack

| Layer | Technology | Status |
|---|---|---|
| Frontend | SvelteKit 2, Svelte 5, TypeScript, Tailwind CSS 4, Inter (bundled) | In use |
| Frontend tests | Vitest, Playwright | In use |
| Backend | ASP.NET Core on .NET 10, C# | In use |
| Backend API docs | Microsoft.AspNetCore.OpenApi, Scalar | In use |
| Database | PostgreSQL, Entity Framework Core (Npgsql) | In use |
| Identity | ASP.NET Core Identity with GUID keys (cookie for web, bearer for mobile) | In use |
| Backend tests | Integration tests with a PostgreSQL fixture | In use |
| Containers and deployment | Podman pod (`backend-pod.yaml`), `Makefile` | In use |
| OpenAPI parsing | Microsoft.OpenApi (OpenAPI.NET) | Planned |
| HTTP fault injection | YARP plus custom middleware and connection handling | Planned; needs proof of concept |
| PDF reports | QuestPDF | Planned |
| Charts | ECharts proposed; the current UI has a custom score ring | **[Open]** |
| CI | GitHub Actions | Planned |
| Mobile client | **[Open]** | Not started |

PostgreSQL is sufficient for V1. Supabase is intentionally not used. A free or low-cost development and deployment path is preferred. Large transient execution streams should not automatically become normalized relational rows.

**Optional infrastructure** (Redis, object storage, a job queue, an observability stack) should be added only when a concrete need appears.

---

# 19. Architecture Style and Backend Modules

ChaosLab V1 is a **modular monolith with separately identifiable execution components**. Reasons: a four-person team, a limited implementation period, and the overhead of distributed systems. Clear boundaries still allow later extraction.

Backend HTTP features are organized as **vertical slices** under `Backend/Features/`, one file per query or command, with routes grouped under `/api` in `Program.cs`. `Features/Projects` is the reference slice.

Implemented slices: `Auth`, `Projects`.

Planned logical modules:

```text
Backend
├── Features/Auth            (implemented)
├── Features/Projects        (implemented)
├── Features/Specifications  (OpenAPI import, endpoint discovery)
├── Features/Environments    (target and credentials)
├── Features/Scenarios       (generation, selection, invariants)
├── Features/Runs            (start, monitor, history)
├── Features/Reports
├── Execution                (coordinator, runner, fault injection)
├── Evaluation               (rules, invariants, findings)
└── Scoring                  (versioned scoring model)
```

The fault injector may start as a module inside the backend and be split into its own project only when technically justified.

---

# 20. Data Model

## 20.1 Current state

Twenty-one tables are mapped in EF Core with an initial migration: the 7 ASP.NET Identity tables and 14 domain tables.

```text
Identity: AspNetUsers (User), AspNetRoles, AspNetUserRoles, AspNetRoleClaims,
          AspNetUserClaims, AspNetUserLogins, AspNetUserTokens
Domain:   Projects, ProjectMembers, ApiSpecs, ApiEndpoints, ApiEnvironments,
          AuthProfiles, ChaosScenarios, TestRuns, ScenarioExecutions,
          HttpObservations, ResilienceFindings, EndpointResults,
          Recommendations, Reports
```

Notable decisions already in the code:

- GUID keys. `User` extends `IdentityUser<Guid>` (username comes from Identity) with first name, last name, display name, `MustChangePassword`, and soft-delete fields.
- Project membership roles `Owner`, `Editor`, `Viewer`; removal is recorded rather than the row being destroyed.
- `ScenarioExecution` snapshots scenario type, parameters, and invariant at execution time.
- `ResilienceFinding` records pattern, outcome, summary, and JSONB evidence, and is separate from raw `HttpObservation` rows.
- `TestRun` stores a `ScoringVersion` (currently `"1.0"`), run configuration (JSONB), status, counts, and score.
- Auth profiles keep non-secret configuration separate from encrypted secret data.
- PDF bytes are stored outside relational rows; the database holds a storage reference.

## 20.2 Planned schema changes (to match the decisions in this document)

1. **Outcomes:** add **Degraded** to `ScenarioOutcome` and `ResilienceFindingOutcome`; add **NotApplicable** to `ScenarioOutcome`.
2. **Category scores:** add a child table (for example `EndpointCategoryScores`: endpoint result, category, score, weight, finding count). Replace the five fixed score columns on `EndpointResult`. Add a run-level category breakdown the same way, plus an evidence-coverage value on `TestRun`.
3. **Categories and patterns:** extend the pattern/category set to the eight scoring categories; keep CircuitBreaker and Fallback as evidence signals.
4. **Invariants:** replace the free-text `DeveloperInvariant` string with a structured assertion (JSONB `type` plus `params`) on `ChaosScenario` and its snapshot on `ScenarioExecution`; store per-run results as findings or finding evidence.
5. **Failure classes:** rename `ObservationOutcomeType.ProxyError` to injector/runner wording, and make the three failure kinds in section 10.3 distinguishable (for example a runner-failure status versus a skipped execution with a recorded reason such as target unreachable).
6. **Observation source:** document that observations can come from direct execution or the injector, not only a proxy.
7. **Deletion rules:** keep historical test evidence protected from accidental cascade deletion.
8. **Concurrency:** record the chosen concurrency in the run configuration.

---

# 21. Repository Layout (actual)

```text
chaos/
├── Backend/                 # ASP.NET Core (.NET 10) API
│   ├── Domain/              # Entities, Enums
│   ├── Features/            # Vertical slices: Auth, Projects
│   ├── Infrastructure/      # Data (DbContext, migrations), Identity
│   ├── Dockerfile, .env.*.example
├── Backend.Tests/           # Integration tests (PostgreSQL fixture)
├── Frontend/                # SvelteKit app
│   └── src/routes/          # (auth): install, login, setup; (app): overview, projects
├── Makefile                 # build, up, down, dev-be, dev-fe, migrate, ...
├── backend-pod.yaml, secret.yaml.example   # Podman pod
├── CHANGELOG.md, CONTEXT.md, FeaturesFlow.md, README.md
```

This replaces the earlier proposed `apps/`, `services/chaos-runner/`, `packages/`, `docs/`, and `infrastructure/` structure. New directories are added only when matching code exists. The fault injector begins inside `Backend` (section 19).

---

# 22. Team Structure

Four members: Neil Jhonreise Vallecer, Christian Dave Sayson, Kristina Cassandra Gonzaga, Jan Nino Rosalijos. Adviser: Mr. Donald M. Francisco.

Confirmed: Neil is the Full Stack Engineer; one teammate is the Documentation Specialist. The remaining assignments below are **recommended ownership, not confirmed**.

- **Full-stack / technical lead:** architecture, backend and frontend, integration, code review.
- **Documentation / research lead:** capstone documentation, requirements, testing evidence, presentation.
- **Backend / chaos engine developer:** execution engine, fault injection, OpenAPI processing, scenario execution, automated tests.
- **Frontend / QA and UX developer:** dashboard UI, results visualization, test workflows, usability and regression testing.

Roles are primary ownership, not hard boundaries.

---

# 23. Current Project Status

Updated from the repository (latest commit: "feat: light mode theme toggle", 8 November 2026).

**Phase:** planning and architecture complete enough to build; early implementation of the platform shell.

**Implemented**

- Instance setup: one-time setup link, `/install` page, transactional SuperAdmin creation.
- Authentication: login (username or email), bearer token and refresh, `me`, change-password, logout, lockout, forced password change, with integration tests.
- Projects: list, create (creator becomes Owner), get, and workspace overview endpoints.
- Frontend: auth pages, session-guarded app shell, overview with score ring and trend, project list, project detail, light/dark theme.
- Database: full EF Core model and initial migration for the entire planned pipeline (schema only).
- Tooling: Podman pod, Makefile, environment templates.

**Not yet built**

- OpenAPI importer and endpoint discovery.
- Environment and credential configuration.
- Scenario generation and selection, including invariants.
- Runner and fault injector.
- Observation capture, evaluation, scoring.
- Recommendations and PDF reports.
- Mobile viewer.
- CI.

The earlier standalone HTML prototype demonstrates the workflow only. Its outcomes are simulated and it is not evidence that the engine exists.

---

# 24. Development Priority

Recommended order:

```text
 1. Project setup                       (done)
 2. Database foundation                 (done: schema; changes in 20.2 pending)
 3. Instance setup, login, projects     (done)
 4. OpenAPI importer
 5. Endpoint discovery
 6. Environment and credential configuration
 7. Basic test runner (direct execution)
 8. Fault injection proof of concept (timeout/drop, slow-drip, dependency failure)
 9. Scenario generation
10. Execution persistence
11. Observation collection
12. Evaluation engine and invariants
13. Resilience scoring (scoring version 1.0)
14. Result dashboard
15. Recommendations
16. PDF reports
17. Mobile read-only viewer
18. Refinement and testing
```

Do not begin with the Resilience Score; reliable observations must exist first.

**First end-to-end milestone:**

```text
Upload OpenAPI → parse one endpoint → configure target API → generate latency scenario
→ execute through ChaosLab → inject latency → capture timing → display result
```

Then add the remaining primitives to the same pipeline, starting with the straightforward ones (5xx, 429, malformed, empty, auth expiry) before the ones that need a proof of concept.

---

# 25. Open Decisions

**Architecture and testing semantics**

1. Confirm section 10.2 (what is under test and where the injector sits for each primitive) after a proof of concept.
2. Prove feasibility of true timeout/drop, connection reset, slow-drip, and dependency-resolution simulation.
3. Define how client retries, backoff, circuit breakers, and fallbacks are observed, and when they are reported Inconclusive.

**Scenarios and data**

4. How valid request bodies and test data are obtained when OpenAPI examples are incomplete.
5. How stateful endpoints are prepared, cleaned up, and safely rerun.
6. Final applicability rules per endpoint and fault.
7. Exact duplicate-request behavior and safety conditions.

**Evaluation and scoring**

8. Thresholds separating Pass, Degraded, and Fail per pattern.
9. Confirm per-outcome points (Pass 100, Degraded 50, Fail 0).
10. Whether severity caps the score; how baselines affect evaluation.
11. Final list of invariant assertion types and the read-back mechanism.
12. Score sensitivity, repeatability, and relation to raw metrics: validation plan.

**Product**

13. Mobile stack and exact viewer screens.
14. Chart library (ECharts or alternative).
15. Which research questions and methodology wording to use: the original RQ1 assumes proxy-based injection and should be reworded to evaluate the actual V1 injection mechanisms and their reproduction accuracy.

**Security and data**

16. Encrypted secret storage, redaction, retention, and telemetry sanitization details.

**Research and defense**

17. Verify every academic reference (Owotogbe et al. 2024, Yang et al. 2024, Aktas et al. 2025 need full bibliographic entries).
18. Define the authorized test environment, repeatable workloads, and usability tasks for evaluation.

---

# 26. Decision Log

Decisions confirmed on 8 November 2026:

| Topic | Decision |
|---|---|
| Architecture | Direct-first baseline with a supporting proxy or simulator; per-primitive placement documented in 10.2 and validated by proof of concept. |
| Result model | Pass, Degraded, Fail, Inconclusive, N/A, with Info/Low/Medium/High/Critical severity. |
| Invariants | In V1, as structured assertions (type plus params) configured in the UI. |
| Score weights | Eight categories locked as scoring version 1.0 (section 14.2). |
| Inconclusive and N/A | Excluded from scoring; evidence coverage reported; empty categories omitted with weight redistributed. |
| Category storage | Category-score child table instead of fixed columns. |
| Concurrency | One experiment at a time by default; configurable up to five. |
| Mobile | Read-only viewer in V1; web is the full client. |
| Repository layout | Documentation follows the actual repo layout (`Backend/`, `Backend.Tests/`, `Frontend/`). |
| Instance setup | One-hour setup link with fragment token; username supplied at setup; no default credentials. |

---

# 27. Positioning, Difference From Other Tools, and Defense Guardrails

**Difference from normal API testing:** traditional testing sends a request and asserts the expected output; ChaosLab understands the API, generates applicable failure scenarios, introduces controlled failures, observes behavior, evaluates resilience, and explains weaknesses.

**Difference from existing tools:** individual tests can be reproduced with Toxiproxy, WireMock, Schemathesis, Playwright, or custom code. ChaosLab's value is automating discovery, scenario applicability, execution, evaluation, scoring, and reporting as one workflow for smaller teams, without requiring infrastructure-level chaos tooling. It may use or integrate with lower-level injection mechanisms.

**Main contribution:** the integration of OpenAPI-driven endpoint and security discovery, scenario applicability analysis, controlled fault injection, runtime behavioral evaluation (built-in rules plus invariants), an explainable score, deterministic recommendations, and exportable reports, in a focused, lower-overhead workflow.

**Do not claim:**

- that ChaosLab invented chaos engineering or fault injection, or is the first or only API resilience tool;
- that "these tests are impossible with existing tools";
- that OpenAPI alone reveals credentials, business rules, dependencies, or runtime behavior;
- that the score is a universal reliability certification;
- that passing every executed scenario proves an API has no resilience defects;
- that ChaosLab can detect any internal retry, circuit breaker, or fallback from a single HTTP response;
- that the library or internal implementation behind observed behavior is known.

---

# 28. Development Principles

- **Keep V1 focused:** a complete vertical slice (`Import → Discover → Generate → Execute → Observe → Evaluate → Report`) beats many unfinished features.
- **Avoid premature microservices:** use module boundaries first.
- **Preserve raw evidence:** every score and recommendation must trace back to recorded observations.
- **Deterministic scoring:** the same observations produce the same score under the same scoring version.
- **Separate evidence from interpretation:** observation, finding, and recommendation are distinct records.
- **Test safely:** especially POST, PUT, PATCH, DELETE, and duplicate-request scenarios.
- **Engineering rule:** does this directly improve ChaosLab's ability to discover, execute, observe, evaluate, or explain API resilience? If not, it probably does not belong in V1.

---

# 29. Current Project Definition

> **ChaosLab is an automated API resilience assurance platform that uses OpenAPI specifications to discover API operations, generates applicable failure scenarios, executes controlled chaos tests against running non-production APIs, observes their response and recovery behavior, and produces explainable resilience scores, findings, recommendations, and reports.**

This document should evolve with the implementation and describe the **actual system being built**, not features that merely sound desirable.
