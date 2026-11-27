using System.Text.Json;
using NexoraRent.Core.Validation;

namespace NexoraRent.Core.Users;

public sealed record RegisterInput(string Email, string Password, string FirstName, string LastName, string? Phone);

public sealed record LoginInput(string Email, string Password);

/// <summary>
/// Reads the bodies of the auth routes with the rules of <c>registerSchema</c>,
/// <c>loginSchema</c> and <c>profileSchema</c> in <c>server/src/routes/auth.routes.js</c>.
/// </summary>
public static class AuthRequest
{
    // bcrypt only uses the first 72 bytes, so Express caps passwords at 72 characters.
    private const int MaxPasswordLength = 72;

    private static readonly StringRules Email = new() { Trim = true, Format = StringFormat.Email };
    private static readonly StringRules Name = new() { Trim = true, MinLength = 1, MaxLength = 100 };
    private static readonly StringRules OptionalName = Name with { Required = false };
    private static readonly StringRules Phone = new() { Required = false, Trim = true, MaxLength = 30 };

    public static (RegisterInput? Input, IReadOnlyList<ValidationIssue> Issues) ReadRegister(JsonElement body)
    {
        var v = new JsonValidator();

        var email = v.ReadString(body, "email", Email);
        var password = v.ReadString(body, "password", new StringRules { MinLength = 12, MaxLength = MaxPasswordLength });
        var firstName = v.ReadString(body, "first_name", Name);
        var lastName = v.ReadString(body, "last_name", Name);
        var phone = v.ReadString(body, "phone", Phone);

        return v.IsValid
            // Express stores `phone || null`, so an empty phone becomes NULL.
            ? (new RegisterInput(Lower(email!), password!, firstName!, lastName!, string.IsNullOrEmpty(phone) ? null : phone), v.Issues)
            : (null, v.Issues);
    }

    public static (LoginInput? Input, IReadOnlyList<ValidationIssue> Issues) ReadLogin(JsonElement body)
    {
        var v = new JsonValidator();

        var email = v.ReadString(body, "email", Email);
        var password = v.ReadString(body, "password", new StringRules { MinLength = 1, MaxLength = MaxPasswordLength });

        return v.IsValid ? (new LoginInput(Lower(email!), password!), v.Issues) : (null, v.Issues);
    }

    public static (ProfileChanges? Changes, IReadOnlyList<ValidationIssue> Issues) ReadProfile(JsonElement body)
    {
        var v = new JsonValidator();

        var firstName = v.ReadString(body, "first_name", OptionalName);
        var lastName = v.ReadString(body, "last_name", OptionalName);
        var phone = v.ReadString(body, "phone", Phone);

        // The object-level refine of profileSchema; zod reports it with an empty path.
        if (!v.Aborted && firstName is null && lastName is null && phone is null)
        {
            v.AddIssue(string.Empty, "At least one field is required");
        }

        return v.IsValid ? (new ProfileChanges(firstName, lastName, phone), v.Issues) : (null, v.Issues);
    }

    // zod's `.transform(value => value.toLowerCase())`. The email pattern only accepts ASCII.
    private static string Lower(string email) => email.ToLowerInvariant();
}
