using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Environments;

// Command: adds a header sent with scenario requests. AppliesTo lists "METHOD /path" operations; empty means all.
public static partial class AddHeader
{
    public sealed record Request(string? Name, string? Value, bool Secret, List<string>? AppliesTo);

    // Set by the HTTP stack or by credentials; a global header must not override them.
    private static readonly string[] Reserved =
        ["Host", "Content-Length", "Transfer-Encoding", "Connection", "Authorization", "Cookie", "Upgrade", "Expect"];

    public static async Task<Results<Created<GetEnvironment.Header>, ValidationProblem, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Request request, ClaimsPrincipal principal, ApplicationDbContext db, CredentialProtector protector,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var name = request.Name?.Trim() ?? "";
        var value = request.Value ?? "";
        var appliesTo = (request.AppliesTo ?? []).Select(operation => operation.Trim()).Where(o => o.Length > 0).Distinct().ToList();
        var errors = new Dictionary<string, string[]>();
        if (name.Length is 0 or > 200 || !Token().IsMatch(name))
            errors["name"] = ["Enter a header name of letters, digits, and - only (at most 200 characters)."];
        else if (Reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
            errors["name"] = [$"{name} is set by ChaosLab or by credentials and cannot be a global header."];
        // HTTP clients refuse to send non-ASCII header values, so reject them here rather than fail at request time.
        if (value.Length is 0 or > 4096 || value.Any(c => c is < ' ' or > '~'))
            errors["value"] = ["Enter a value of at most 4096 printable ASCII characters, with no line breaks."];
        if (appliesTo.Count > 50 || appliesTo.Any(operation => !Operation().IsMatch(operation)))
            errors["appliesTo"] = ["List up to 50 operations like \"POST /payments\", or leave empty for all requests."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var environmentId = await db.ApiEnvironments.Where(environment => environment.ProjectId == id && environment.IsDefault)
            .Select(environment => (Guid?)environment.Id).SingleOrDefaultAsync(cancellationToken);
        if (environmentId is null)
            return TypedResults.Conflict("Save a target API before adding headers.");

        var header = new EnvironmentHeader
        {
            Id = Guid.NewGuid(), EnvironmentId = environmentId.Value, Name = name,
            AppliesTo = JsonSerializer.SerializeToDocument(appliesTo)
        };
        if (request.Secret)
        {
            header.SecretCipherText = protector.Protect(value, header.Id);
            header.Hint = CredentialProtector.Hint(value);
        }
        else
            header.Value = value;
        db.EnvironmentHeaders.Add(header);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict($"A header named {name} already exists.");
        }
        return TypedResults.Created($"/api/projects/{id}/environment/headers/{header.Id}",
            new GetEnvironment.Header(header.Id, name, header.Value, request.Secret, header.Hint, appliesTo));
    }

    // RFC 9110 token characters.
    [GeneratedRegex(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$")]
    private static partial Regex Token();

    [GeneratedRegex(@"^(GET|PUT|POST|DELETE|OPTIONS|HEAD|PATCH|TRACE) /\S{0,1023}$")]
    private static partial Regex Operation();
}
