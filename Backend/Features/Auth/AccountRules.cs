using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Backend.Features.Auth;

// Account field validation shared by first-run setup and SuperAdmin account creation.
public static class AccountRules
{
    public static Dictionary<string, string[]> Validate(
        string? username, string? email, string? firstName, string? lastName, string? password)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateField(username, "username", 256);
        ValidateField(email, "email", 256);
        ValidateField(firstName, "firstName", 100);
        ValidateField(lastName, "lastName", 100);
        if (!string.IsNullOrWhiteSpace(email) && !new EmailAddressAttribute().IsValid(email.Trim()))
            errors["email"] = ["A valid email address is required."];
        if (string.IsNullOrWhiteSpace(password))
            errors["password"] = ["Password is required."];
        return errors;

        void ValidateField(string? value, string field, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors[field] = ["This field is required."];
            else if (value.Trim().Length > maximumLength)
                errors[field] = [$"Must be at most {maximumLength} characters."];
        }
    }

    public static ValidationProblem IdentityValidation(IdentityResult result) =>
        TypedResults.ValidationProblem(result.Errors
            .GroupBy(error => error.Code.Contains("Password", StringComparison.Ordinal) ? "password"
                : error.Code.Contains("UserName", StringComparison.Ordinal) ? "username"
                : error.Code.Contains("Email", StringComparison.Ordinal) ? "email" : "user")
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
}
