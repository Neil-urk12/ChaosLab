# Changelog

All notable changes to ChaosLab are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). No versions have been
released yet, so history is grouped by date.

## [Unreleased]

### Added

- `ConnectionStrings:DefaultConnection` placeholders for Development, Testing,
  Staging, and Production, with README instructions for supplying connection strings
  through environment variables or deployment secrets.
- Initial SuperAdmin bootstrap: ASP.NET Core Identity (`UserManager`/`RoleManager`)
  provisioned at startup from `BootstrapAdmin:*` configuration, creating the
  `SuperAdmin` role and one account with `MustChangePassword = true`. Creation is
  transactional, serializable, and retried on concurrent startup; an existing usable
  SuperAdmin makes the configuration optional.

### Changed

- Bundle the Inter font with the frontend (`@fontsource-variable/inter`) instead of
  loading it from Google Fonts, so auth pages make no third-party requests and work
  offline. The font is imported once in the root layout. The package is a
  `devDependency`, like the rest of the frontend, because Vite bundles it at build time.
- Drop the unneeded `!` important modifier from the submit-button margins on the
  login and setup pages. Tailwind v4 `space-y-*` no longer styles the last child.

### Removed

- Google Fonts `<link>` tags from the `(auth)` layout.

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
