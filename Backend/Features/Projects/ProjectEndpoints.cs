namespace Backend.Features.Projects;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder api)
    {
        var projects = api.MapGroup("/projects").WithTags("Projects");
        projects.MapGet("/", ListProjects.Handle);
        projects.MapPost("/", CreateProject.Handle);
        projects.MapGet("/overview", GetOverview.Handle);
        projects.MapGet("/{id:guid}", GetProject.Handle);
    }
}
