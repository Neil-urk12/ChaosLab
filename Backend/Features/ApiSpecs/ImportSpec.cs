using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.ApiSpecs;

// Command: validates an OpenAPI document and, when it has no errors, makes it the project's active spec.
public static class ImportSpec
{
    public const string FetchClient = "spec-fetch";

    public sealed record Request(ApiSpecSourceType? Source, string? Content, string? Url, string? Filename);

    public sealed record Report(string? Filename, string? OpenApiVersion, int OperationCount, List<SpecIssue> Issues);

    public static async Task<Results<Created<Report>, UnprocessableEntity<Report>, ValidationProblem, NotFound, ForbidHttpResult, Conflict>> Handle(
        Guid id, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        string? content;
        string? filename;
        Uri? url = null;
        switch (request.Source)
        {
            case ApiSpecSourceType.Url:
                if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out url)
                    || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
                    return Invalid("url", "Enter an absolute http(s) URL without credentials.");
                filename = Path.GetFileName(url.AbsolutePath);
                // Private and loopback hosts stay reachable (test targets are usually internal); CreateFetchHandler
                // blocks metadata/link-local addresses. Only parse issues are returned, never the fetched body.
                using (var client = httpClientFactory.CreateClient(FetchClient))
                {
                    try
                    {
                        content = await client.GetStringAsync(url, cancellationToken);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                        && exception is HttpRequestException or TaskCanceledException)
                    {
                        var reason = exception switch
                        {
                            HttpRequestException { StatusCode: HttpStatusCode status } => $"the server responded with {(int)status}",
                            TaskCanceledException => "the request timed out",
                            _ => "it could not be reached or is larger than 5 MB"
                        };
                        return TypedResults.UnprocessableEntity(new Report(filename, null, 0,
                            [new(IssueSeverity.Error, "url", $"The document could not be fetched: {reason}.")]));
                    }
                }
                break;
            case ApiSpecSourceType.Paste or ApiSpecSourceType.FileUpload:
                content = request.Content;
                filename = request.Source == ApiSpecSourceType.FileUpload ? Path.GetFileName(request.Filename?.Trim()) : null;
                if (string.IsNullOrWhiteSpace(content))
                    return Invalid("content", "Paste or upload an OpenAPI document.");
                break;
            default:
                return Invalid("source", "Source must be Paste, FileUpload, or Url.");
        }

        if (string.IsNullOrWhiteSpace(content))
            return TypedResults.UnprocessableEntity(new Report(filename, null, 0,
                [new(IssueSeverity.Error, "document", "The document is empty.")]));
        if (Encoding.UTF8.GetByteCount(content) > SpecDocument.MaxBytes)
            return Invalid("content", "The document must be at most 5 MB.");
        filename = string.IsNullOrEmpty(filename) ? null : filename[..Math.Min(filename.Length, 255)];

        var (parsed, issues) = await SpecDocument.ReadAsync(content, cancellationToken);
        if (parsed is null)
            return TypedResults.UnprocessableEntity(new Report(filename, null, 0, issues));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = await db.ApiSpecs.SingleOrDefaultAsync(spec => spec.ProjectId == id && spec.IsActive, cancellationToken);
        if (previous is not null)
        {
            // Never-test and excluded choices survive re-import for the same path and method.
            var testing = await db.ApiEndpoints.AsNoTracking()
                .Where(endpoint => endpoint.ApiSpecId == previous.Id)
                .ToDictionaryAsync(endpoint => (endpoint.Path, endpoint.HttpMethod), endpoint => endpoint.Testing, cancellationToken);
            foreach (var endpoint in parsed.Endpoints)
                endpoint.Testing = testing.GetValueOrDefault((endpoint.Path, endpoint.HttpMethod));

            // The one-active-spec index is checked per statement, so clear the old flag first.
            previous.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
        }

        var spec = new ApiSpec
        {
            Id = Guid.NewGuid(),
            ProjectId = id,
            Title = parsed.Title,
            VersionLabel = parsed.VersionLabel,
            OpenApiVersion = parsed.OpenApiVersion,
            SourceFilename = filename,
            SourceType = request.Source!.Value,
            SourceUrl = url?.ToString(),
            SourceFormat = parsed.Format,
            OriginalDocument = content,
            ParsedDocument = parsed.Document,
            SpecHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            UploadedById = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            Endpoints = parsed.Endpoints
        };
        db.ApiSpecs.Add(spec);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another import for this project committed first.
            return TypedResults.Conflict();
        }
        // Scenarios are not generated here: the user starts generation from the Scenarios page (FeaturesFlow §2.13).
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Created($"/api/projects/{id}/spec",
            new Report(filename, parsed.OpenApiVersion, parsed.Endpoints.Count, issues));
    }

    // Checks every resolved address at connect time, so redirects and DNS rebinding cannot bypass it.
    public static SocketsHttpHandler CreateFetchHandler() => new()
    {
        // Through a proxy the callback would only see the proxy's address, so HTTP(S)_PROXY is ignored.
        UseProxy = false,
        MaxAutomaticRedirections = 3,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(IsBlocked))
                throw new HttpRequestException("Destination not allowed.");
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    // Cloud metadata services outside link-local: AWS IPv6 IMDS and Alibaba Cloud.
    private static readonly IPAddress[] MetadataHosts = [IPAddress.Parse("fd00:ec2::254"), IPAddress.Parse("100.100.100.200")];
    // NAT64 well-known prefix 64:ff9b::/96; the last four bytes are the IPv4 destination.
    private static readonly byte[] Nat64Prefix = [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];

    // Link-local (169.254/16 incl. cloud metadata, fe80::/10), unspecified, multicast, and known metadata hosts
    // are never spec hosts.
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address)
            || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal
            || MetadataHosts.Contains(address))
            return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes.AsSpan(0, 12).SequenceEqual(Nat64Prefix) && IsBlocked(new IPAddress(bytes.AsSpan(12)));
        return bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || bytes[0] >= 224;
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
