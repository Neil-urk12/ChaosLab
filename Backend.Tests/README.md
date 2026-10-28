# Backend automated tests

`Backend.Tests` targets .NET 10 and is included in `Backend/Backend.sln`. It uses
xUnit with the VSTest runner, `Microsoft.AspNetCore.Mvc.Testing`, and the
[Testcontainers PostgreSQL module](https://dotnet.testcontainers.org/modules/postgres/).
Persistence tests use real PostgreSQL, with no EF InMemory provider.

## Run

Install the .NET 10 SDK and start a Docker API compatible container runtime.
Docker Desktop/Engine can use the default endpoint. With the repository's
rootless Podman convention on Linux, start the user API socket and configure
Testcontainers for it:

```sh
systemctl --user start podman.socket
export DOCKER_HOST="unix://${XDG_RUNTIME_DIR}/podman/podman.sock"
export TESTCONTAINERS_RYUK_DISABLED=true
```

The [Testcontainers configuration](https://dotnet.testcontainers.org/custom_configuration/)
supports these variables. Ryuk is disabled only for rootless Podman; fixtures
still dispose their containers. Leave Ryuk enabled for Docker. An alternative
to the user socket is running `podman system service --time=0` on a dedicated
Unix socket and setting `DOCKER_HOST` to that socket.

Run from the repository root:

```sh
cd Backend
dotnet restore
dotnet build
dotnet test
```

The first test run downloads `postgres:17.11-alpine` if absent. No configured
database, local secrets file, or manually applied migrations are needed. Missing
container infrastructure fails the suite instead of silently skipping tests.

## Structure and isolation

| Location | Purpose |
| --- | --- |
| `Infrastructure/PostgreSqlFixture.cs` | One test-owned PostgreSQL container per xUnit collection, randomly generated password, unique database per test, migration application, database and container cleanup. |
| `Infrastructure/PersistenceTest.cs` | Per-test EF context and valid seed graph; asserts PostgreSQL SQLSTATE and the specific constraint for rejected writes. |
| `Infrastructure/TestData.cs` | Small valid graph spanning two projects and multiple environments/endpoints so tests can vary a single ownership invariant. |
| `Infrastructure/BackendFactory.cs` | ASP.NET Core test host; replaces EF registrations with the disposable database and uses the Testing environment. |
| `Integration/ModelInvariantTests.cs` | Membership uniqueness, active/default partial indexes, environment names, composite foreign keys, historical inactive specifications, and jsonb round-trip/operator behavior. |
| `Integration/CheckConstraintTests.cs` | Every score column, inclusive score boundaries and nullable scores, scenario counts, execution sequence, observation attempt, and report version. |
| `Integration/BackendHostTests.cs` | Existing HTTP endpoint smoke test and confirmation that the host's EF context uses the migrated disposable database. |

The fixture derives all connections from its own container. It never reads
`ConnectionStrings__DefaultConnection` or an existing application database. Each
test creates `chaoslab_test_<random GUID>`, applies the checked-in migrations with
`MigrateAsync`, and drops that database during disposal. Connection pooling is
disabled so contexts release their database connections immediately. The
collection disposes the container and its anonymous storage when the run ends.
No container reuse or persistent development volume is configured.

Seed data is saved and detached before each persistence test. Duplicate-key and
ownership failures therefore reach PostgreSQL instead of passing through an EF
tracking exception or relationship fixup. Invalid cases assert both SQLSTATE
and constraint name; valid cases prove that partial indexes and optional
relationships permit the intended data. Every theory case gets its own database.

To add a persistence test, join `PostgreSqlCollection`, inherit `PersistenceTest`,
and use its `Db` and `Data` members. For an HTTP test, create a database with
`PostgreSqlFixture.CreateDatabaseAsync()` and pass it to `BackendFactory`; dispose
the factory before the database. The factory follows the
[ASP.NET Core integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
pattern. `Program` is public/partial to expose the entry point to `WebApplicationFactory`.
The factory supplies generated test-only bootstrap credentials through in-memory
configuration (including explicit null overrides when testing missing secrets).
`InitialAdminBootstrapTests` covers Identity provisioning, validation, transaction
rollback, concurrent startup, preservation of existing users and host restart after
secret removal. It reuses the same disposable PostgreSQL fixture and migrations.
