namespace Backend.Features.Environments;

public static class EnvironmentEndpoints
{
    public static void MapEnvironmentEndpoints(this IEndpointRouteBuilder api)
    {
        var environment = api.MapGroup("/projects/{id:guid}/environment").WithTags("Target environment");
        environment.MapGet("/", GetEnvironment.Handle);
        environment.MapPut("/", SaveTarget.Handle);
        environment.MapPost("/check", CheckConnection.Handle);
        environment.MapPut("/credentials/{scheme}", SaveCredential.Handle);
        environment.MapDelete("/credentials/{scheme}", RemoveCredential.Handle);
        environment.MapPost("/credentials/{scheme}/validate", ValidateCredential.Handle);
        environment.MapPost("/headers", AddHeader.Handle);
        environment.MapPatch("/headers/{headerId:guid}", UpdateHeader.Handle);
        environment.MapDelete("/headers/{headerId:guid}", UpdateHeader.Remove);
    }
}
