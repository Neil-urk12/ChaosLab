using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.ApiSpecs;

namespace Backend.Features.Environments;

// Sends one request to a target API: the connection test and credential validation.
public static class TargetClient
{
    private const string Verified = "target";
    private const string Unverified = "target-insecure";

    public sealed record Probe(int? Status, string? Error, long ElapsedMs);

    public static void AddTargetClients(this IServiceCollection services)
    {
        // Same address guard as the spec fetch. Redirects are not followed, so credentials never leave the target.
        services.AddHttpClient(Verified, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => Handler(verifyTls: true));
        services.AddHttpClient(Unverified, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => Handler(verifyTls: false));
    }

    private static SocketsHttpHandler Handler(bool verifyTls)
    {
        var handler = ImportSpec.CreateFetchHandler();
        handler.AllowAutoRedirect = false;
        if (!verifyTls)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return handler;
    }

    public static async Task<Probe> SendAsync(
        IHttpClientFactory factory, HttpRequestMessage request, bool verifyTls, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(factory, verifyTls);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return new((int)response.StatusCode, null, watch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException)
        {
            return new(null, Describe(exception, timeoutSeconds), watch.ElapsedMilliseconds);
        }
    }

    public static HttpClient CreateClient(IHttpClientFactory factory, bool verifyTls) =>
        factory.CreateClient(verifyTls ? Verified : Unverified);

    // Messages are fixed text: exception details could echo internal addresses or certificate data.
    public static string Describe(Exception exception, int timeoutSeconds) => exception switch
    {
        TaskCanceledException => $"No response within {timeoutSeconds} s.",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
            "The TLS handshake failed. Turn off certificate verification only for self-signed test hosts.",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "The host name could not be resolved.",
        _ => "The target could not be reached, or its address is not allowed."
    };

    // Joins the base URL and an operation path: "https://h/v1" + "/payments" -> "https://h/v1/payments".
    // Null when the result would not be on the base URL's scheme, host, and port, so credentials stay with the target.
    public static Uri? Combine(string baseUrl, string path) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var root)
        && Uri.TryCreate(baseUrl.TrimEnd('/') + path, UriKind.Absolute, out var url)
        && url.GetLeftPart(UriPartial.Authority) == root.GetLeftPart(UriPartial.Authority)
            ? url : null;

    // Whether a saved base URL and another one share scheme, host and port: the boundary credentials are tied to.
    public static bool SameOrigin(string saved, Uri next) =>
        Uri.TryCreate(saved, UriKind.Absolute, out var old)
        && old.GetLeftPart(UriPartial.Authority).Equals(next.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    public static string? ValidateBaseUrl(string? value, out Uri? url)
    {
        url = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            return "Enter an absolute http(s) URL.";
        if (parsed.UserInfo.Length > 0 || parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
            return "The base URL cannot contain credentials, a query, or a fragment.";
        if (parsed.OriginalString.Length > 2048)
            return "The base URL must be at most 2048 characters.";
        url = parsed;
        return null;
    }

    // Applies a credential the way its spec scheme describes: an apiKey goes in the query, a cookie, or a header.
    public static void Authenticate(HttpRequestMessage request, AuthType type, string secret, string? username, SchemeInfo? scheme)
    {
        switch (type)
        {
            case AuthType.Bearer:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                break;
            case AuthType.Basic:
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{secret}")));
                break;
            case AuthType.ApiKey:
                var name = scheme?.ParameterName ?? "X-API-Key";
                if (scheme?.In is "query")
                {
                    var builder = new UriBuilder(request.RequestUri!);
                    var pair = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(secret)}";
                    // Appended, so the operation's own query parameters stay.
                    builder.Query = builder.Query.Length > 1 ? $"{builder.Query[1..]}&{pair}" : pair;
                    request.RequestUri = builder.Uri;
                }
                else if (scheme?.In is "cookie")
                    request.Headers.TryAddWithoutValidation("Cookie", $"{name}={secret}");
                else
                {
                    // The credential wins over a global header of the same name; adding both would merge them into one value.
                    request.Headers.Remove(name);
                    request.Headers.TryAddWithoutValidation(name, secret);
                }
                break;
        }
    }
}
