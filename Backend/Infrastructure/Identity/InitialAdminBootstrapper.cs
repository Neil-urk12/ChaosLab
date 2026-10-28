using System.Data;
using Backend.Domain.Entities;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Backend.Infrastructure.Identity;

public sealed class InitialAdminBootstrapper(
    ApplicationDbContext db,
    UserManager<User> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<BootstrapAdminOptions> options) : IInitialAdminBootstrapper
{
    public const string SuperAdminRole = "SuperAdmin";

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        // Serializable isolation protects the absence check, including competing usernames.
        // Identity saves through this same scoped context; all writes commit together.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var commitAttempted = false;
            try
            {
                if (!await roles.RoleExistsAsync(SuperAdminRole))
                    RequireSuccess(await roles.CreateAsync(new IdentityRole<Guid>(SuperAdminRole)
                    {
                        Id = Guid.NewGuid()
                    }), "SuperAdmin role creation");

                var admins = await users.GetUsersInRoleAsync(SuperAdminRole);
                if (!admins.Any(user => !user.IsDeleted && !string.IsNullOrWhiteSpace(user.UserName)
                    && !string.IsNullOrWhiteSpace(user.PasswordHash)))
                {
                    var config = options.Value;
                    Validate(config);
                    var user = new User
                    {
                        Id = Guid.NewGuid(),
                        UserName = config.Username,
                        Firstname = config.FirstName!,
                        Lastname = config.LastName!,
                        DisplayName = $"{config.FirstName} {config.LastName}",
                        Email = string.IsNullOrWhiteSpace(config.Email) ? null : config.Email,
                        MustChangePassword = true
                    };
                    RequireSuccess(await users.CreateAsync(user, config.Password!),
                        "BootstrapAdmin user creation (Username, Email or Password)");
                    RequireSuccess(await users.AddToRoleAsync(user, SuperAdminRole),
                        "SuperAdmin role assignment");
                }

                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (IsRetryableRace(exception))
            {
                // PostgreSQL ends the transaction when COMMIT fails with a retryable race.
                // Write-time failures still need an explicit rollback.
                if (!commitAttempted)
                    await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                if (attempt == 2)
                    throw new InvalidOperationException(
                        "Initial SuperAdmin bootstrap encountered concurrent database writes. Restart to retry.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    private static bool IsRetryableRace(Exception exception)
    {
        // Npgsql's execution strategy can wrap DbUpdateException in InvalidOperationException.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres)
                return postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation;
        return false;
    }

    private static void RequireSuccess(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            // Descriptions can echo input. Codes identify policy failures without exposing values.
            throw new InvalidOperationException($"{operation} failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
    }

    private static void Validate(BootstrapAdminOptions config)
    {
        RequireField(config.Username, nameof(config.Username), 256);
        RequireField(config.Password, nameof(config.Password));
        RequireField(config.FirstName, nameof(config.FirstName), 100);
        RequireField(config.LastName, nameof(config.LastName), 100);
        if (config.Email?.Length > 256)
            throw new InvalidOperationException("BootstrapAdmin:Email must be at most 256 characters.");
    }

    private static void RequireField(string? value, string field, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"BootstrapAdmin:{field} is required when no active SuperAdmin exists.");
        if (maxLength.HasValue && value.Length > maxLength.Value)
            throw new InvalidOperationException($"BootstrapAdmin:{field} must be at most {maxLength.Value} characters.");
    }
}
