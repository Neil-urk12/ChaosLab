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
