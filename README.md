# ChaosLab

> **Automated API Resilience, Failure Testing, and Assurance Platform**

ChaosLab is an automated resilience testing platform designed to evaluate how RESTful APIs and their host applications behave under controlled failure conditions before deployment. 

By importing an **OpenAPI 3.x / Swagger specification**, ChaosLab discovers endpoints and schemas, generates context-aware failure scenarios, executes them against a running API in non-production environments, observes behavior, and delivers evidence-backed resilience scores, actionable findings, and exportable reports.

---

## Table of Contents

- [The Problem](#the-problem)
- [How It Works](#how-it-works)
- [Core Capabilities](#core-capabilities)
  - [The 9 Chaos Primitives](#the-9-chaos-primitives)
  - [Idempotency & Duplicate Testing](#idempotency--duplicate-testing)
  - [Resilience Patterns Evaluated](#resilience-patterns-evaluated)
- [System Architecture](#system-architecture)
- [Tech Stack](#tech-stack)
- [Repository Structure](#repository-structure)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Environment Configuration](#environment-configuration)
  - [Running with Podman (Recommended)](#running-with-podman-recommended)
  - [Running Services Locally](#running-services-locally)
- [Makefile Commands](#makefile-commands)
- [Safety & Testing Scope](#safety--testing-scope)
- [Project Documentation](#project-documentation)

---

## The Problem

Traditional API functional testing verifies whether an endpoint works under ideal conditions:
- *Does `POST /orders` create an order and return 201?*
- *Does `GET /users/{id}` match the schema?*

However, production failures usually stem from operational degradation:
- What happens when a downstream dependency experiences high latency?
- Does the client or upstream service retry boundedly with exponential backoff, or trigger a retry storm?
- Does the service respect `429 Too Many Requests` and honor `Retry-After` headers?
- Are timeouts enforced, or do hanging operations exhaust connection pools?
- How does the application handle empty or malformed JSON payloads?
- Can duplicate requests create accidental duplicate transactions?

**ChaosLab bridges the gap** by systematically applying controlled failures to running APIs and measuring observable resilience behavior.

---

## How It Works

```text
OpenAPI Specification (JSON / YAML)
              │
              ▼
        API Discovery
   (Endpoints, Schemas, Auth)
              │
              ▼
     Scenario Generation
 (Method & Parameter-aware Tests)
              │
              ▼
   Chaos / Failure Injection
   (ChaosLab Proxy / Runner)
              │
              ▼
     Target Running API
   (Dev / Staging / Non-Prod)
              │
              ▼
 Observation & Evidence Collection
    (Timing, Status Codes, Drops)
              │
              ▼
    Resilience Evaluation
  (Scores, Findings, Violations)
              │
              ▼
     Reports & Remediation
 (Interactive Dashboard & Export)
```

1. **Import & Discover**: Upload an OpenAPI 3.x specification to automatically parse endpoints, HTTP methods, request/response schemas, and authentication schemes.
2. **Generate Scenarios**: ChaosLab generates applicable resilience tests based on endpoint semantics and characteristics.
3. **Execute & Inject**: Test traffic is routed through the failure injection engine against the target service.
4. **Observe & Evaluate**: Network timing, HTTP responses, retries, and errors are recorded and evaluated against resilience heuristics.
5. **Score & Report**: ChaosLab computes a transparent 0–100 Resilience Score and provides recommendations for remediation.

---

## Core Capabilities

### The 9 Chaos Primitives

ChaosLab V1 incorporates nine primary failure injection primitives:

| Primitive | Description | Resilience Focus |
|:---|:---|:---|
| **Latency** | Artificially inject delays into request/response paths | Timeout enforcement, slow dependency handling, retry behavior |
| **5xx Storm** | Inject repeated server failures (`500`, `502`, `503`) | Bounded retries, backoff, circuit breaking, fallback |
| **429 Throttle** | Return rate limit status codes with `Retry-After` | Rate-limit compliance, backoff timing |
| **Timeout / Drop** | Drop connections or sever responses mid-flight | Deadlines, cancellation token propagation, resource cleanup |
| **Malformed JSON** | Return corrupted, incomplete, or invalid JSON syntax | Parser resilience, error boundaries, schema assumptions |
| **Empty Response** | Return successful HTTP status with an empty body | Null handling, missing payload assumptions |
| **Slow Drip** | Stream payload bytes at an extremely slow rate | Read timeouts, connection pool exhaustion |
| **Auth Expiry** | Simulate `401 Unauthorized` and `403 Forbidden` | Token refresh flows, credential recovery |
| **DNS / Dependency Failure**| Emulate unresolvable or offline downstream targets | Graceful degradation, error bubbling |

### Idempotency & Duplicate Testing

Separately from the chaos primitives, ChaosLab generates duplicate request scenarios (such as concurrent or sequential identical `POST` payloads) to evaluate side-effect deduplication and idempotency safeguards.

### Resilience Patterns Evaluated

ChaosLab adheres to an **observable evidence philosophy**—evaluating black-box behavior rather than assuming internal code implementations:
- **Retry Logic & Backoff**: Evaluates retry counts, intervals, and exponential backoff spacing.
- **Timeout Enforcement**: Verifies operations terminate within configured execution budgets.
- **Circuit Breaking**: Detects cessation of outbound requests following repeated target failures.
- **Graceful Fallbacks**: Assesses whether degraded alternative responses are returned during failures.

---

## System Architecture

ChaosLab is structured as a **modular monolith** with clean module separation:

```text
                  ┌───────────────────────────────┐
                  │      Frontend (SvelteKit)     │
                  │   UI Dashboard, Config, Docs  │
                  └───────────────┬───────────────┘
                                  │ HTTP / REST
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ChaosLab Backend (.NET 10)                   │
├────────────────────────────────┬────────────────────────────────┤
│ • Auth & User Management       │ • Scenario Generator           │
│ • Project & API Catalog        │ • Chaos Proxy & Runner         │
│ • OpenAPI Discovery & Parser   │ • Observation & Evidence Store │
│ • Target Environment Config    │ • Evaluation & Scoring Engine  │
└────────────────────────────────┬────────────────────────────────┘
                                 │
           ┌─────────────────────┴─────────────────────┐
           ▼                                           ▼
┌──────────────────────┐                    ┌─────────────────────┐
│  PostgreSQL Database │                    │   Target Test API   │
│ Persistence & Scores │                    │  (Non-Prod Server)  │
└──────────────────────┘                    └─────────────────────┘
```

---

## Tech Stack

- **Frontend**: [SvelteKit](https://svelte.dev/) (Svelte 5), TypeScript, [Tailwind CSS v4](https://tailwindcss.com/), Vite, Vitest, Playwright.
- **Backend**: [ASP.NET Core (.NET 10)](https://learn.microsoft.com/en-us/dotnet/core/), Entity Framework Core, Scalar OpenAPI documentation.
- **Database**: [PostgreSQL 17](https://www.postgresql.org/).
- **Containerization & Tooling**: [Podman](https://podman.io/) (with `podman play kube`), Make.

---

## Repository Structure

```text
chaos/
├── Backend/                 # ASP.NET Core Web API (.NET 10)
│   ├── Classes/             # Shared helper classes and models
│   ├── Domain/              # Domain entities, enums, and interfaces
│   │   ├── Entities/        # Users, Projects, ApiSpecs, TestRuns, etc.
│   │   └── Enums/           # Scenarios, AuthType, TestRunStatus, etc.
│   ├── Infrastructure/      # Data access, EF Core ApplicationDbContext
│   ├── Properties/          # Launch profiles & settings
│   ├── Dockerfile           # Backend container build specification
│   ├── Backend.csproj       # Project dependencies & .NET target
│   └── Program.cs           # Application entry point & middleware pipeline
├── Frontend/                # SvelteKit web application
│   ├── src/                 # Application routes, components, and libraries
│   │   ├── routes/          # SvelteKit page routes
│   │   └── lib/             # Shared UI components and utilities
│   ├── static/              # Static public assets
│   ├── package.json         # Frontend dependencies and scripts
│   └── vite.config.ts       # Vite configuration
├── backend-pod.yaml         # Kubernetes/Podman pod definition for dev stack
├── secret.yaml.example      # Example secrets for database and credentials
├── Makefile                 # Developer automation commands
├── PROJECT.md               # Full architecture design & project specification
├── FeaturesFlow.md          # End-to-end user experience and feature flow
└── README.md                # Project overview and quickstart guide
```

---

## Getting Started

### Prerequisites

Ensure you have the following installed on your machine:
- **[Podman](https://podman.io/)** (or Docker with podman-compatible CLI)
- **`envsubst`** (typically available via `gettext` package)
- **Make**
- **Node.js** (v20+) & **[pnpm](https://pnpm.io/)** (for frontend development)
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** (optional, for local backend compilation)

---

### Environment Configuration

1. Initialize your local secrets file from the provided example:
   ```bash
   make secret-init
   ```
2. (Optional) Adjust database credentials in `secret.yaml` if needed:
   ```yaml
   apiVersion: v1
   kind: Secret
   metadata:
     name: chaoslab-secret
   type: Opaque
   stringData:
     postgres-password: your_secure_password
     connection-string: "Host=localhost;Port=5432;Database=chaos_db;Username=postgres;Password=your_secure_password"
   ```

The backend includes `ConnectionStrings:DefaultConnection` placeholders in
`Backend/appsettings.{Environment}.json` for `Development`, `Testing`, `Staging`,
and `Production`. Leave these values `null`; supply each environment's connection
string through `ConnectionStrings__DefaultConnection` using local environment
variables or deployment secrets. Never commit real credentials.

Select the environment with `ASPNETCORE_ENVIRONMENT` (for example, `Staging`).
For local runs outside Development, use `dotnet run --no-launch-profile` so the
Development launch profile does not override it. Integration tests select
`Testing` and inject their own isolated Testcontainers database connection.

The first startup against an empty database also requires
`BootstrapAdmin__Username`, `BootstrapAdmin__Password`, `BootstrapAdmin__FirstName`,
and `BootstrapAdmin__LastName` (optionally `BootstrapAdmin__Email`) as environment
variables or deployment secrets. The account is created with the `SuperAdmin` role
and `MustChangePassword = true`. There are no default credentials, and nothing is
created if an active SuperAdmin already exists, so remove the variables after the
first successful start.

---

### Running with Podman (Recommended)

1. **Build the Backend Container Image**:
   ```bash
   make build
   ```
2. **Start the Pod Stack** (PostgreSQL + Backend API):
   ```bash
   make up
   ```
3. **Check Running Containers**:
   ```bash
   make ps
   ```
4. **Follow Logs**:
   ```bash
   make logs          # All containers
   make logs-backend  # Backend only
   make logs-db       # Postgres only
   ```
5. **Run the Frontend in Development Mode**:
   ```bash
   make dev-fe
   ```
   Open [http://localhost:5173](http://localhost:5173) in your browser. The backend API is reachable at [http://localhost:8080](http://localhost:8080).

6. **Tear Down the Stack**:
   ```bash
   make down
   ```
   *Note: Database volume data is preserved across `make down`. To wipe database volumes, run `make clean-data`.*

---

### Running Services Locally

#### Backend
```bash
cd Backend
dotnet restore
dotnet run
```
API endpoints and Scalar documentation will be accessible at `http://localhost:5000` (or `http://localhost:8080` depending on your environment profile).

#### Frontend
```bash
cd Frontend
pnpm install
pnpm run dev
```

---

## Makefile Commands

| Command | Description |
|:---|:---|
| `make help` | Display available commands |
| `make secret-init` | Generate `secret.yaml` from `secret.yaml.example` if missing |
| `make build` | Build the backend container image (`localhost/backend:latest`) |
| `make up` | Start the Podman pod stack (`chaoslab-stack`) |
| `make down` | Stop and delete the pod stack (keeps database data) |
| `make restart` | Restart the pod stack (`make down && make up`) |
| `make dev-fe` | Launch the SvelteKit frontend development server |
| `make ps` | List status of active pods and containers |
| `make logs` | Stream logs for all containers in `chaoslab-stack` |
| `make logs-backend`| Stream logs specifically from the backend container |
| `make logs-db` | Stream logs specifically from the PostgreSQL container |
| `make clean-data` | Stop the stack and delete persistent PostgreSQL data |

---

## Safety & Testing Scope

> [!WARNING]
> **Controlled Environments Only**: ChaosLab is engineered strictly for authorized testing against development, staging, or dedicated non-production environments.
> 
> Never execute failure injection or duplicate request tests against production systems or third-party APIs without explicit consent and safeguards.

---

## Project Documentation

For in-depth specifications and design details:
- [PROJECT.md](file:///home/imisenak/Documents/Capstonks/chaos/PROJECT.md): Full project specification, design decisions, primitives, resilience scoring, and long-term roadmap.
- [FeaturesFlow.md](file:///home/imisenak/Documents/Capstonks/chaos/FeaturesFlow.md): Detailed user flows, system interactions, and web/mobile journey specifications.
