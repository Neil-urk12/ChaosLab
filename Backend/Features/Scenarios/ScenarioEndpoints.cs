namespace Backend.Features.Scenarios;

public static class ScenarioEndpoints
{
    public static void MapScenarioEndpoints(this IEndpointRouteBuilder api)
    {
        var scenarios = api.MapGroup("/projects/{id:guid}/scenarios").WithTags("Scenarios");
        scenarios.MapGet("/", GetScenarios.Handle);
        scenarios.MapPost("/generate", GenerateScenarios.Handle);
        scenarios.MapPatch("/{scenarioId:guid}", UpdateScenario.Handle);
    }
}
