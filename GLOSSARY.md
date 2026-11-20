# ChaosLab Community

ChaosLab tests how a running API behaves under controlled failure, and turns what it observes into findings, a Resilience Score and recommendations. This glossary covers the self-hosted installation.

## Language

### People and access

**SuperAdmin**:
The system role that administers a ChaosLab instance and creates its user accounts. It is separate from any project role.
_Avoid_: Admin, root

**Project**:
A workspace for testing one API. It holds that API's spec, environments, scenarios and test runs.
_Avoid_: Workspace, app

**Project membership**:
A user's Owner, Editor, or Viewer access to one project. Removing and re-adding a member changes the same membership.
_Avoid_: Share, project role assignment

### The API under test

**Target API**:
The running API that ChaosLab tests. ChaosLab never tests a spec on its own, and never a production target.
_Avoid_: System under test, service, backend

**Environment**:
A project's record of where its target API runs and how to reach it, such as the base URL, kind, headers, credentials and dependency.
_Avoid_: API configuration, target configuration, server

**Environment kind**:
Development, Test, Staging, or Production mirror. A Production mirror is an isolated copy of production, never production itself.

**Authorization confirmation**:
A user's statement that they own the target API or are authorized to test it.

**Auth profile**:
The test credentials and scheme (Bearer token, API key, Basic, or none) that ChaosLab uses to call the target API in one environment.
_Avoid_: Login, account, auth config

**Dependency**:
A service the target API calls. An environment names at most one, by its dependency URL.
_Avoid_: Downstream, upstream service

**Dependency simulator**:
ChaosLab's stand-in for the dependency. The target is configured to call it instead. It passes calls through to the dependency, and cuts them while a dependency failure is being injected.
_Avoid_: Proxy, mock

### Specs and endpoints

**Spec**:
An imported OpenAPI 3.x document that describes the target API. It is used to plan scenarios and never replaces a running target.
_Avoid_: Swagger, schema, contract, definition

**Spec re-import**:
Importing a newer version of a project's spec. Endpoints and scenarios follow the new version; an identical document changes nothing.
_Avoid_: Spec update, refresh

**Endpoint**:
One HTTP method on one path of the spec, for example `POST /orders`.
_Avoid_: Operation, route, path

**Endpoint testing**:
Whether an endpoint is Included, Excluded, or Never test. Excluded leaves it out by choice; Never test marks it as unsafe to call at all.

### Scenarios

**Chaos primitive**:
One of exactly nine kinds of failure ChaosLab injects: Latency, 5xx Storm, 429 Throttle, Timeout / Drop, Malformed JSON, Empty Response, Slow Drip, Authentication Expiry, and DNS / Dependency-Resolution Failure.
_Avoid_: Fault type, chaos type, attack

**Duplicate request**:
A behavioral scenario that sends the same state-changing request more than once to test idempotency. It is not a chaos primitive.
_Avoid_: Replay, race test

**Fault**:
One failure applied during a scenario execution, such as a delayed response or a cut dependency call. The request it is applied to is the faulted request.
_Avoid_: Error, chaos

**Baseline**:
A normal request, with no fault, whose result later requests are compared against. It is not a chaos primitive.
_Avoid_: Control request, warm-up

**Recovery request**:
A normal request sent after a fault ends, to see whether the target API recovered.

**Chaos scenario**:
A generated, configurable test for one endpoint, using one chaos primitive or a duplicate request, together with the reason it was generated. Its default selection is a suggestion for future runs.
_Avoid_: Test case, experiment

**Invariant**:
A structured, developer-defined assertion about business correctness, attached to a scenario. Example: "at most one order per idempotency key".
_Avoid_: Rule, check, expectation

**Read-back**:
An optional request, sent after a scenario's requests, that fetches the resulting state so an invariant can check it.
_Avoid_: Verification request

### Running

**Test run**:
One execution of selected scenarios against one environment. It keeps a snapshot of its configuration and its scoring version.
_Avoid_: Job, test session

**Scenario execution**:
One selected scenario executed within a test run, keeping the scenario configuration used at execution time.
_Avoid_: Attempt, experiment

**Runner**:
The part of ChaosLab that sends a test run's requests to the target API.

**Runner-path fault**:
A fault applied between the runner and the target API. It shows what the runner sees, not how the target handles a failing dependency.

**Dependency-path fault**:
A fault applied to the target API's own calls to its dependency, through the dependency simulator.

**Observation**:
One raw, recorded fact about a request in a scenario execution: what was sent, what came back, its timing, and what was injected. Observations are evidence and hold no judgment.
_Avoid_: Log, trace, result

**Target failure**:
The target API behaving badly during a scenario execution. This is a legitimate result.

**Runner failure**:
An error inside ChaosLab while executing a scenario. It is never counted against the target API.
_Avoid_: Test failure

**Unreachable target**:
A target API that could not be reached before a scenario ran. The scenario is skipped with a reason and is not scored.

### Evaluation

**Resilience pattern**:
A behavior ChaosLab looks for evidence of: Retry, Timeout, Circuit breaker, Fallback, Idempotency, Rate-limit, Authentication failure, Response validation, or Dependency failure. Backoff is evidence judged under Retry, not a pattern of its own. Fallback is the pattern for graceful degradation and is scored in the Graceful Degradation category.
_Avoid_: Policy, strategy

**Resilience finding**:
An evidence-backed judgment of one endpoint's behavior for one resilience pattern in a test run, with an outcome, a severity and a scoring category. A scenario execution can produce several.
_Avoid_: Result, issue, bug

**Outcome**:
The verdict of a finding: Pass, Degraded, Fail, Inconclusive, or Not applicable. A scenario's outcome is that of its worst finding. Runner failures and unreachable targets are not outcomes.
_Avoid_: Status, Passed, Failed

**Inconclusive**:
The outcome when observable evidence is not enough to judge. ChaosLab never guesses instead.
_Avoid_: Unknown, error

**Severity**:
How serious a finding is: Info, Low, Medium, High, or Critical. A recommendation carries the severity of its finding.
_Avoid_: Priority, impact

**Recommendation**:
A deterministic, rule-based suggestion for fixing one Fail or Degraded finding. Each such finding gets exactly one, and every recommendation belongs to a finding. ChaosLab never changes the target's code.
_Avoid_: Fix, advice

### Scoring

**Resilience Score**:
A deterministic, explainable 0–100 score for a completed test run, built from weighted category scores. It is not a certification.
_Avoid_: Health score, reliability score, grade

**Scoring category**:
One of eight weighted groups the Resilience Score is built from: Timeout Handling, Retry Behavior, Graceful Degradation, Rate-Limit Handling, Idempotency, Authentication Failure Handling, Response Validation, and Dependency-Failure Handling.

**Scored finding**:
A finding whose outcome is Pass, Degraded, or Fail. Inconclusive and Not applicable findings are unscored.

**Category score**:
The mean of one scoring category's scored findings in a test run.

**Endpoint score**:
The Resilience Score worked out from one endpoint's findings only, using the same scoring categories.
_Avoid_: Endpoint grade, pattern score

**Evidence coverage**:
The share of a run's findings, excluding Not applicable, that are scored. It shows how much evidence a score rests on.

**Scoring version**:
The locked set of category weights and outcome points a test run is scored with.

### Output

**Report**:
An exported PDF of one test run's results, with sensitive values redacted.
_Avoid_: Export, summary
