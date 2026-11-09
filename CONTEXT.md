# ChaosLab Community

ChaosLab evaluates API resilience in a self-hosted installation.

## Language

**Project membership**: A user's Owner, Editor, or Viewer access to a project. Removing and re-adding a member changes the same membership; project access is separate from system roles such as SuperAdmin.

**Chaos scenario**: A generated, configurable test for a discovered endpoint. Its default selection is a suggestion for future runs.

**Scenario execution**: One selected scenario executed within a TestRun, retaining the scenario configuration used at execution time.

**Resilience finding**: Evidence about an endpoint's Retry, Timeout, CircuitBreaker, Fallback, or Idempotency behavior in a TestRun. Its outcome can be Passed, Degraded, Failed, Inconclusive, or NotApplicable, and it carries a severity (Info, Low, Medium, High, Critical).

**DuplicateRequest**: An idempotency test scenario, separate from the nine chaos primitives.
