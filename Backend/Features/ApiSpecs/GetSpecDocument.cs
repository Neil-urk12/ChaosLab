using System.Security.Claims;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.ApiSpecs;

// Query: the active spec exactly as it was imported.
public static class GetSpecDocument
{
    public static async Task<Results<ContentHttpResult, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, HttpResponse response, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        var document = await db.ApiSpecs.AsNoTracking()
            .Where(spec => spec.ProjectId == id && spec.IsActive)
            .Select(spec => spec.OriginalDocument)
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null)
            return TypedResults.NotFound();

        // Untrusted text: plain text with nosniff so a browser never renders it as HTML.
        response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Text(document, "text/plain; charset=utf-8");
    }
}
