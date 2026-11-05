namespace Backend.Infrastructure.Identity;

public sealed class BootstrapSetupOptions
{
    public const string SectionName = "BootstrapSetup";

    public string PublicUrl { get; set; } = "http://localhost:5173";

    public static bool IsValidPublicUrl(string publicUrl) =>
        Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !publicUrl.Contains('?') && !publicUrl.Contains('#')
        && string.IsNullOrEmpty(uri.UserInfo);
}
