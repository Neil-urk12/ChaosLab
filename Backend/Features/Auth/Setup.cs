using System.ComponentModel.DataAnnotations;
using Backend.Domain.Entities;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Auth;

public static class Setup
{
    private const long SetupLock = 0x4348414F53534554;

    public sealed record Request(
        string? Token, string? Username, string? Email, string? FirstName, string? LastName, string? Password);

    public static async Task<Results<NoContent, Conflict, StatusCodeHttpResult, ValidationProblem>> Handle(
        Request request, ApplicationDbContext db, UserManager<User> users, RoleManager<IdentityRole<Guid>> roles,
        SignInManager<User> signIn, BootstrapSetupToken token, CancellationToken cancellationToken)
    {
        if (await roles.RoleExistsAsync(AuthSetup.SuperAdminRole))
            return TypedResults.Conflict();
        if (!token.IsValid(request.Token))
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        // Identity stores share this context; the transaction lock also serializes setup across backend instances.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({SetupLock})", cancellationToken);

        if (await roles.RoleExistsAsync(AuthSetup.SuperAdminRole))
            return TypedResults.Conflict();
        if (!token.IsValid(request.Token))
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        var errors = new Dictionary<string, string[]>();
        ValidateField(request.Username, "username", 256);
        ValidateField(request.Email, "email", 256);
        ValidateField(request.FirstName, "firstName", 100);
        ValidateField(request.LastName, "lastName", 100);
        if (!string.IsNullOrWhiteSpace(request.Email) && !new EmailAddressAttribute().IsValid(request.Email.Trim()))
            errors["email"] = ["A valid email address is required."];
        if (string.IsNullOrWhiteSpace(request.Password))
            errors["password"] = ["Password is required."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var roleResult = await roles.CreateAsync(new IdentityRole<Guid>(AuthSetup.SuperAdminRole)
        {
            Id = Guid.NewGuid()
        });
        if (!roleResult.Succeeded)
            return IdentityValidation(roleResult);

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.Username!.Trim(),
            Email = request.Email!.Trim(),
            Firstname = request.FirstName!.Trim(),
            Lastname = request.LastName!.Trim(),
            DisplayName = $"{request.FirstName.Trim()} {request.LastName.Trim()}",
            MustChangePassword = false
        };
        var userResult = await users.CreateAsync(user, request.Password!);
        if (!userResult.Succeeded)
            return IdentityValidation(userResult);
        var assignmentResult = await users.AddToRoleAsync(user, AuthSetup.SuperAdminRole);
        if (!assignmentResult.Succeeded)
            return IdentityValidation(assignmentResult);

        await transaction.CommitAsync(cancellationToken);
        signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.NoContent();

        void ValidateField(string? value, string field, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors[field] = ["This field is required."];
            else if (value.Trim().Length > maximumLength)
                errors[field] = [$"Must be at most {maximumLength} characters."];
        }
    }

    private static ValidationProblem IdentityValidation(IdentityResult result) =>
        TypedResults.ValidationProblem(result.Errors
            .GroupBy(error => error.Code.Contains("Password", StringComparison.Ordinal) ? "password"
                : error.Code.Contains("UserName", StringComparison.Ordinal) ? "username"
                : error.Code.Contains("Email", StringComparison.Ordinal) ? "email" : "user")
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
}
