using Backend.Domain.Entities;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class ModelInvariantTests(PostgreSqlFixture postgres) : PersistenceTest(postgres)
{
    [Fact]
    public async Task Project_membership_rejects_duplicate_project_user_pair()
    {
        Db.ProjectMembers.Add(new ProjectMember { ProjectId = Data.Project.Id, UserId = Data.User.Id });
        await AssertViolationAsync(PostgresErrorCodes.UniqueViolation, "PK_ProjectMembers");
    }

    [Fact]
    public async Task Project_membership_allows_same_user_in_another_project_and_another_user_in_same_project()
    {
        var otherUser = new User { Id = Guid.NewGuid(), Firstname = "Other", Lastname = "User" };
        Db.Users.Add(otherUser);
        Db.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = Data.OtherProject.Id, UserId = Data.User.Id },
            new ProjectMember { ProjectId = Data.Project.Id, UserId = otherUser.Id });
        await Db.SaveChangesAsync();
        Assert.Equal(3, await Db.ProjectMembers.CountAsync());
    }

    [Fact]
    public async Task Project_rejects_a_second_active_specification()
    {
        Db.ApiSpecs.Add(Data.NewSpec(Data.Project.Id));
        await AssertViolationAsync(PostgresErrorCodes.UniqueViolation, "IX_ApiSpecs_ProjectId");
    }

    [Fact]
    public async Task Project_allows_multiple_inactive_specifications_beside_the_active_specification()
    {
        Db.ApiSpecs.AddRange(Data.NewSpec(Data.Project.Id, false), Data.NewSpec(Data.Project.Id, false));
        await Db.SaveChangesAsync();
        Assert.Equal(3, await Db.ApiSpecs.CountAsync(x => x.ProjectId == Data.Project.Id));
        Assert.Equal(1, await Db.ApiSpecs.CountAsync(x => x.ProjectId == Data.Project.Id && x.IsActive));
    }

    [Fact]
    public async Task Activating_another_specification_while_one_is_active_is_rejected()
    {
        var inactive = Data.NewSpec(Data.Project.Id, false);
        Db.ApiSpecs.Add(inactive);
        await Db.SaveChangesAsync();
        inactive.IsActive = true;
        await AssertViolationAsync(PostgresErrorCodes.UniqueViolation, "IX_ApiSpecs_ProjectId");
    }

    [Fact]
    public async Task Project_rejects_a_second_default_environment()
    {
        Db.ApiEnvironments.Add(Data.NewEnvironment(Data.Project.Id, "Second default", true));
        await AssertViolationAsync(PostgresErrorCodes.UniqueViolation, "IX_ApiEnvironments_ProjectId");
    }

    [Fact]
    public async Task Project_allows_multiple_nondefault_environments()
    {
        Db.ApiEnvironments.Add(Data.NewEnvironment(Data.Project.Id, "Third environment"));
        await Db.SaveChangesAsync();
        Assert.Equal(3, await Db.ApiEnvironments.CountAsync(x => x.ProjectId == Data.Project.Id));
        Assert.Equal(1, await Db.ApiEnvironments.CountAsync(x => x.ProjectId == Data.Project.Id && x.IsDefault));
    }

    [Fact]
    public async Task Making_another_environment_default_while_one_is_default_is_rejected()
    {
        var environment = await Db.ApiEnvironments.SingleAsync(x => x.Id == Data.AlternateEnvironment.Id);
        environment.IsDefault = true;
        await AssertViolationAsync(PostgresErrorCodes.UniqueViolation, "IX_ApiEnvironments_ProjectId");
    }

    [Fact]
    public async Task Environment_names_must_be_unique_within_a_project()
    {
        Db.ApiEnvironments.Add(Data.NewEnvironment(Data.Project.Id, Data.Environment.Name));
        await AssertViolationAsync(PostgresErrorCodes.UniqueViolation, "IX_ApiEnvironments_ProjectId_Name");
    }

    [Fact]
    public async Task Environment_name_can_be_reused_in_another_project()
    {
        // Both projects independently persist their default environment named Development.
        Assert.Equal(2, await Db.ApiEnvironments.CountAsync(x => x.Name == "Development"));
    }

    [Fact]
    public async Task TestRun_rejects_specification_from_another_project()
    {
        var run = Data.NewRun();
        run.ApiSpecId = Data.OtherSpec.Id;
        Db.TestRuns.Add(run);
        await AssertViolationAsync(PostgresErrorCodes.ForeignKeyViolation, "FK_TestRuns_ApiSpecs_ApiSpecId_ProjectId");
    }

    [Fact]
    public async Task TestRun_rejects_environment_from_another_project()
    {
        var run = Data.NewRun();
        run.EnvironmentId = Data.OtherEnvironment.Id;
        Db.TestRuns.Add(run);
        await AssertViolationAsync(PostgresErrorCodes.ForeignKeyViolation, "FK_TestRuns_ApiEnvironments_EnvironmentId_ProjectId");
    }

    [Fact]
    public async Task TestRun_rejects_authentication_profile_from_another_environment_in_same_project()
    {
        var run = Data.NewRun();
        run.AuthProfileId = Data.OtherAuthProfile.Id;
        Db.TestRuns.Add(run);
        await AssertViolationAsync(PostgresErrorCodes.ForeignKeyViolation, "FK_TestRuns_AuthProfiles_AuthProfileId_EnvironmentId");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestRun_accepts_no_authentication_or_a_profile_from_its_environment(bool authenticated)
    {
        var run = Data.NewRun();
        run.AuthProfileId = authenticated ? Data.AuthProfile.Id : null;
        Db.TestRuns.Add(run);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        Assert.Equal(run.AuthProfileId, (await Db.TestRuns.SingleAsync(x => x.Id == run.Id)).AuthProfileId);
    }

    [Fact]
    public async Task ScenarioExecution_rejects_a_scenario_for_another_endpoint()
    {
        var execution = Data.NewExecution();
        execution.SequenceNo = 2;
        execution.EndpointId = Data.OtherEndpoint.Id;
        Db.ScenarioExecutions.Add(execution);
        await AssertViolationAsync(PostgresErrorCodes.ForeignKeyViolation, "FK_ScenarioExecutions_ChaosScenarios_ScenarioId_EndpointId");
    }

    [Fact]
    public async Task ScenarioExecution_accepts_matching_scenario_and_endpoint()
    {
        var execution = Data.NewExecution();
        execution.SequenceNo = 2;
        Db.ScenarioExecutions.Add(execution);
        await Db.SaveChangesAsync();
        Assert.Equal(2, await Db.ScenarioExecutions.CountAsync());
    }

    [Fact]
    public async Task Deactivating_specification_preserves_historical_TestRun_and_allows_referencing_that_version()
    {
        var spec = await Db.ApiSpecs.SingleAsync(x => x.Id == Data.Spec.Id);
        spec.IsActive = false;
        await Db.SaveChangesAsync();
        Db.ApiSpecs.Add(Data.NewSpec(Data.Project.Id));
        var historicalRun = Data.NewRun();
        Db.TestRuns.Add(historicalRun);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var existingRun = await Db.TestRuns.Include(x => x.ApiSpec).SingleAsync(x => x.Id == Data.Run.Id);
        Assert.Equal(Data.Spec.Id, existingRun.ApiSpecId);
        Assert.False(existingRun.ApiSpec.IsActive);
        var newReference = await Db.TestRuns.Include(x => x.ApiSpec).SingleAsync(x => x.Id == historicalRun.Id);
        Assert.Equal(Data.Spec.Id, newReference.ApiSpecId);
        Assert.False(newReference.ApiSpec.IsActive);
    }

    [Fact]
    public async Task Specification_json_document_round_trips_as_queryable_jsonb()
    {
        var spec = await Db.ApiSpecs.SingleAsync(x => x.Id == Data.Spec.Id);
        Assert.Equal("Test API", spec.ParsedDocument.RootElement.GetProperty("info").GetProperty("title").GetString());
        var stored = await Db.Database.SqlQuery<string>($"""
            SELECT pg_typeof("ParsedDocument")::text || ':' || ("ParsedDocument" -> 'info' ->> 'title') AS "Value"
            FROM "ApiSpecs" WHERE "Id" = {Data.Spec.Id}
            """).SingleAsync();
        Assert.Equal("jsonb:Test API", stored);
    }
}
