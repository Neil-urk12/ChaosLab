using Backend.Domain.Entities;
using Backend.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Users;

// Command: the SuperAdmin creates an account with a temporary password the user must change at first sign-in.
public static class CreateUser
{
    public sealed record Request(string? Username, string? Email, string? FirstName, string? LastName, string? Password);

    public sealed record Response(Guid Id, string UserName, string DisplayName, string Email);

    public static async Task<Results<Created<Response>, ValidationProblem>> Handle(
        Request request, UserManager<User> users)
    {
        var errors = AccountRules.Validate(
            request.Username, request.Email, request.FirstName, request.LastName, request.Password);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.Username!.Trim(),
            Email = request.Email!.Trim(),
            Firstname = request.FirstName!.Trim(),
            Lastname = request.LastName!.Trim(),
            DisplayName = $"{request.FirstName.Trim()} {request.LastName.Trim()}",
            MustChangePassword = true
        };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, request.Password!);
        }
        // Identity checks usernames but not emails; the unique indexes catch a duplicate email and a concurrent duplicate username.
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "EmailIndex" or "UserNameIndex" } duplicate)
        {
            return TypedResults.ValidationProblem(duplicate.ConstraintName == "EmailIndex"
                ? new Dictionary<string, string[]> { ["email"] = ["An account with this email already exists."] }
                : new Dictionary<string, string[]> { ["username"] = ["This username is already taken."] });
        }
        if (!result.Succeeded)
            return AccountRules.IdentityValidation(result);

        return TypedResults.Created((string?)null, new Response(user.Id, user.UserName, user.DisplayName, user.Email));
    }
}
