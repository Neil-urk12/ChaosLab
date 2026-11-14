using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.TestRuns;

// Command: creates a Queued run from the selected scenarios of the active spec's included endpoints against the
// default environment (FeaturesFlow 2.15). The run keeps a configuration snapshot and each execution a snapshot
// of its scenario, so later edits don't change history. Scenarios that call POST/PUT/PATCH/DELETE or send
// duplicate requests need explicit confirmation. RunExecutor picks the Queued run up (2.16).
public static class CreateRun
{
    // The locked scoring version new runs are scored with.
    public const string ScoringVersion = "1.0";
    public const int MaxConcurrency = 5;
    public const int MaxRunnerRetries = 3;
    public const int MaxScenarios = 2000;

    public sealed record Request(
        List<Guid>? ScenarioIds, int? Concurrency, bool? BaselineRequests, int? RunnerRetries, bool? ConfirmStateChanging);

    public sealed record Response(Guid Id);

    public static bool IsStateChanging(string method) => method is "POST" or "PUT" or "PATCH" or "DELETE";

    public static async Task<Results<Created<Response>, ValidationProblem, NotFound, ForbidHttpResult, UnprocessableEntity<string>>> Handle(
        Guid id, Request request, ClaimsPrincipal principal, ApplicationDbContext db, IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var ids = request.ScenarioIds?.Distinct().ToList() ?? [];
        var concurrency = request.Concurrency ?? 1;
        var retries = request.RunnerRetries ?? 0; // RunExecutor never retries yet, so the snapshot says so.
        var errors = new Dictionary<string, string[]>();
        if (ids.Count == 0) errors["scenarioIds"] = ["Select at least one scenario."];
        else if (ids.Count > MaxScenarios) errors["scenarioIds"] = [$"A run can have at most {MaxScenarios} scenarios."];
        if (concurrency is < 1 or > MaxConcurrency) errors["concurrency"] = [$"Must be between 1 and {MaxConcurrency}."];
        if (retries is < 0 or > MaxRunnerRetries) errors["runnerRetries"] = [$"Must be between 0 and {MaxRunnerRetries}."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var specId = await db.ApiSpecs.Where(spec => spec.ProjectId == id && spec.IsActive)
            .Select(spec => (Guid?)spec.Id).SingleOrDefaultAsync(cancellationToken);
        var environment = await db.ApiEnvironments.AsNoTracking()
            .SingleOrDefaultAsync(environment => environment.ProjectId == id && environment.IsDefault, cancellationToken);
        // Included endpoints are checked through the selected scenarios below.
        if (GetPreflight.Blockers(environment, null, specId is not null, hasIncluded: true).FirstOrDefault() is { } blocker)
            return TypedResults.UnprocessableEntity(blocker);

        // Only scenarios of included endpoints of the active spec can run; anything else means a stale page.
        var scenarios = await db.ChaosScenarios.AsNoTracking()
            .Where(scenario => ids.Contains(scenario.Id) && scenario.Endpoint.ApiSpecId == specId
                && scenario.Endpoint.Testing == EndpointTesting.Included)
            .OrderBy(scenario => scenario.Endpoint.Path).ThenBy(scenario => scenario.Endpoint.HttpMethod)
            .ThenBy(scenario => scenario.ScenarioType).ThenBy(scenario => scenario.Id)
            .Select(scenario => new
            {
                scenario.Id, scenario.EndpointId, scenario.Endpoint.HttpMethod, scenario.ScenarioType,
                scenario.Parameters, scenario.Invariants
            })
            .ToListAsync(cancellationToken);
        if (scenarios.Count != ids.Count)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["scenarioIds"] = ["Some selected scenarios are no longer in this project's included endpoints. Reload and try again."]
            });

        var stateChanging = scenarios.Count(scenario =>
            IsStateChanging(scenario.HttpMethod) || scenario.ScenarioType == ScenarioType.DuplicateRequest);
        if (stateChanging > 0 && request.ConfirmStateChanging != true)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["confirmStateChanging"] = ["Confirm the state-changing scenarios before starting the run."]
            });

        // Checked last, right before creating, so nothing runs against a target that is down.
        var reachability = await GetPreflight.ProbeAsync(environment!, httpClientFactory, cancellationToken);
        if (!reachability.Reachable)
            return TypedResults.UnprocessableEntity($"The target is unreachable: {reachability.Error ?? "no response"}");

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var run = new TestRun
        {
            ProjectId = id,
            ApiSpecId = specId!.Value,
            EnvironmentId = environment!.Id,
            Status = TestRunStatus.Queued,
            ScoringVersion = ScoringVersion,
            TotalScenarios = scenarios.Count,
            RequestedById = userId,
            RunConfig = JsonSerializer.SerializeToDocument(new
            {
                concurrency,
                baselineRequests = request.BaselineRequests ?? true,
                runnerRetries = retries,
                stateChangingScenarios = stateChanging,
                stateChangingConfirmed = stateChanging > 0,
                target = new
                {
                    environment.Name, environment.BaseUrl, Kind = environment.Kind.ToString(), environment.VerifyTls,
                    environment.RequestTimeoutSeconds
                },
                endpointIds = scenarios.Select(scenario => scenario.EndpointId).Distinct(),
                scenarioIds = scenarios.Select(scenario => scenario.Id)
            }, JsonSerializerOptions.Web)
        };
        var sequence = 0;
        foreach (var scenario in scenarios)
            run.ScenarioExecutions.Add(new ScenarioExecution
            {
                EndpointId = scenario.EndpointId,
                ScenarioId = scenario.Id,
                SequenceNo = ++sequence,
                ScenarioType = scenario.ScenarioType,
                Parameters = scenario.Parameters,
                Invariants = scenario.Invariants
            });
        db.TestRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/projects/{id}/runs/{run.Id}", new Response(run.Id));
    }
}
