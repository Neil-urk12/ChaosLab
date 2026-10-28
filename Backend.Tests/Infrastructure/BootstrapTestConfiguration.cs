using System.Security.Cryptography;
using Backend.Infrastructure.Identity;

namespace Backend.Tests.Infrastructure;

internal static class BootstrapTestConfiguration
{
    public static BootstrapAdminOptions Valid() => new()
    {
        Username = "installation-admin",
        Password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}",
        FirstName = "Initial",
        LastName = "Administrator"
    };

    public static Dictionary<string, string?> Values(BootstrapAdminOptions options) => new()
    {
        ["BootstrapAdmin:Username"] = options.Username,
        ["BootstrapAdmin:Password"] = options.Password,
        ["BootstrapAdmin:FirstName"] = options.FirstName,
        ["BootstrapAdmin:LastName"] = options.LastName,
        ["BootstrapAdmin:Email"] = options.Email
    };
}
