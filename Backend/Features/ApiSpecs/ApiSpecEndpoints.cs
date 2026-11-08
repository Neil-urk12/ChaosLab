namespace Backend.Features.ApiSpecs;

public static class ApiSpecEndpoints
{
    public static void MapApiSpecEndpoints(this IEndpointRouteBuilder api)
    {
        var spec = api.MapGroup("/projects/{id:guid}/spec").WithTags("API specification");
        spec.MapGet("/", GetSpec.Handle);
        // Body limit leaves room for JSON escaping of a 5 MB document.
        spec.MapPost("/", ImportSpec.Handle).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(12 * 1024 * 1024));
        spec.MapGet("/document", GetSpecDocument.Handle);
        spec.MapPatch("/endpoints/{endpointId:guid}", SetEndpointTesting.Handle);
    }
}
