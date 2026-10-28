using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Infrastructure.Data;

namespace Backend.Tests.Infrastructure;

// A valid persistence graph. Tests vary one invariant at a time using existing IDs.
internal sealed class TestData
{
    public User User { get; } = new() { Id = Guid.NewGuid(), Firstname = "Test", Lastname = "User" };
    public Project Project { get; }
    public Project OtherProject { get; }
    public ApiSpec Spec { get; }
    public ApiSpec OtherSpec { get; }
    public ApiEnvironment Environment { get; }
    public ApiEnvironment OtherEnvironment { get; }
    public ApiEnvironment AlternateEnvironment { get; }
    public AuthProfile AuthProfile { get; }
    public AuthProfile OtherAuthProfile { get; }
    public ApiEndpoint Endpoint { get; }
    public ApiEndpoint OtherEndpoint { get; }
    public ChaosScenario Scenario { get; }
    public TestRun Run { get; }
    public ScenarioExecution Execution { get; }

    public TestData()
    {
        Project = new Project { Id = Guid.NewGuid(), Name = "Project", CreatedById = User.Id };
        OtherProject = new Project { Id = Guid.NewGuid(), Name = "Other project", CreatedById = User.Id };
        Spec = NewSpec(Project.Id);
        OtherSpec = NewSpec(OtherProject.Id);
        Environment = NewEnvironment(Project.Id, "Development", true);
        OtherEnvironment = NewEnvironment(OtherProject.Id, "Development", true);
        AlternateEnvironment = NewEnvironment(Project.Id, "Staging");
        AuthProfile = NewAuthProfile(Environment.Id);
        OtherAuthProfile = NewAuthProfile(AlternateEnvironment.Id);
        Endpoint = NewEndpoint("/first");
        OtherEndpoint = NewEndpoint("/second");
        Scenario = new ChaosScenario { Id = Guid.NewGuid(), EndpointId = Endpoint.Id, Parameters = Json("{}") };
        Run = NewRun();
        Execution = NewExecution();
    }

    public async Task SeedAsync(ApplicationDbContext context)
    {
        context.AddRange(User, Project, OtherProject, Spec, OtherSpec, Environment, OtherEnvironment,
            AlternateEnvironment, AuthProfile, OtherAuthProfile, Endpoint, OtherEndpoint, Scenario, Run, Execution,
            new ProjectMember { ProjectId = Project.Id, UserId = User.Id });
        await context.SaveChangesAsync();
        // Let PostgreSQL enforce invalid FKs/duplicate keys, rather than EF tracking/fixup intercepting them.
        context.ChangeTracker.Clear();
    }

    public ApiSpec NewSpec(Guid projectId, bool active = true) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId, UploadedById = User.Id, IsActive = active,
        OpenApiVersion = "3.0.3", OriginalDocument = "{\"openapi\":\"3.0.3\"}",
        ParsedDocument = Json("{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"Test API\"}}")
    };

    public ApiEnvironment NewEnvironment(Guid projectId, string name, bool isDefault = false) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId, Name = name, IsDefault = isDefault,
        CreatedById = User.Id, BaseUrl = "https://example.invalid"
    };

    public TestRun NewRun() => new()
    {
        Id = Guid.NewGuid(), ProjectId = Project.Id, ApiSpecId = Spec.Id, EnvironmentId = Environment.Id,
        RequestedById = User.Id, RunConfig = Json("{}")
    };

    public ScenarioExecution NewExecution() => new()
    {
        Id = Guid.NewGuid(), TestRunId = Run.Id, EndpointId = Endpoint.Id, ScenarioId = Scenario.Id,
        SequenceNo = 1, Parameters = Json("{}")
    };

    public EndpointResult NewEndpointResult() => new()
    {
        Id = Guid.NewGuid(), TestRunId = Run.Id, EndpointId = Endpoint.Id, OverallScore = 50,
        Evidence = Json("{}")
    };

    private AuthProfile NewAuthProfile(Guid environmentId) => new()
    {
        Id = Guid.NewGuid(), EnvironmentId = environmentId, Name = "Test authentication", PublicConfig = Json("{}")
    };

    private ApiEndpoint NewEndpoint(string path) => new()
    {
        Id = Guid.NewGuid(), ApiSpecId = Spec.Id, Path = path, HttpMethod = "GET", Tags = Json("[]"),
        Parameters = Json("[]"), Responses = Json("{}")
    };

    private static JsonDocument Json(string value) => JsonDocument.Parse(value);
}
