using System.Text.Json;
using NexoraRent.Core.Users;
using NexoraRent.Core.Validation;

namespace NexoraRent.UnitTests;

/// <summary>
/// The expected issues are the exact output of registerSchema, loginSchema and profileSchema
/// in server/src/routes/auth.routes.js (zod 4) for the same input.
/// </summary>
public class AuthRequestTests
{
    [Fact]
    public void Register_reads_trimmed_lowercased_values_and_drops_an_empty_phone()
    {
        var (input, issues) = AuthRequest.ReadRegister(Parse("""
            {"email":" Ana@Example.TEST ","password":"long-enough-pass","first_name":" Ana ","last_name":"B","phone":"","role":"owner"}
            """));

        Assert.Empty(issues);
        Assert.Equal(new RegisterInput("ana@example.test", "long-enough-pass", "Ana", "B", null), input);
    }

    [Fact]
    public void Register_without_fields_lists_each_required_field()
    {
        var (input, issues) = AuthRequest.ReadRegister(Parse("{}"));

        Assert.Null(input);
        Assert.Equal(
            [
                new("email", "Invalid input: expected string, received undefined"),
                new("password", "Invalid input: expected string, received undefined"),
                new("first_name", "Invalid input: expected string, received undefined"),
                new("last_name", "Invalid input: expected string, received undefined"),
            ],
            issues);
    }

    [Fact]
    public void Register_reports_every_invalid_field_like_zod()
    {
        var (input, issues) = AuthRequest.ReadRegister(Parse($$"""
            {"email":" NOPE ","password":"short","first_name":"  ","last_name":"{{new string('x', 101)}}","phone":5}
            """));

        Assert.Null(input);
        Assert.Equal(
            [
                new("email", "Invalid email address"),
                new("password", "Too small: expected string to have >=12 characters"),
                new("first_name", "Too small: expected string to have >=1 characters"),
                new("last_name", "Too big: expected string to have <=100 characters"),
                new("phone", "Invalid input: expected string, received number"),
            ],
            issues);
    }

    [Fact]
    public void Register_rejects_a_password_longer_than_bcrypt_uses()
    {
        var (_, issues) = AuthRequest.ReadRegister(Parse($$"""
            {"email":"a@b.co","password":"{{new string('p', 73)}}","first_name":"A","last_name":"B"}
            """));

        Assert.Equal([new ValidationIssue("password", "Too big: expected string to have <=72 characters")], issues);
    }

    [Fact]
    public void Login_reads_a_lowercased_email()
    {
        var (input, issues) = AuthRequest.ReadLogin(Parse("""{"email":" Owner@Example.Test","password":" spaces kept "}"""));

        Assert.Empty(issues);
        Assert.Equal(new LoginInput("owner@example.test", " spaces kept "), input);
    }

    [Fact]
    public void Login_errors_match_zod()
    {
        Assert.Equal(
            [
                new("email", "Invalid input: expected string, received undefined"),
                new("password", "Invalid input: expected string, received undefined"),
            ],
            AuthRequest.ReadLogin(Parse("{}")).Issues);

        Assert.Equal(
            [
                new("email", "Invalid email address"),
                new("password", "Too small: expected string to have >=1 characters"),
            ],
            AuthRequest.ReadLogin(Parse("""{"email":"a@b","password":""}""")).Issues);
    }

    [Fact]
    public void Profile_needs_at_least_one_field()
    {
        var (changes, issues) = AuthRequest.ReadProfile(Parse("""{"email":"ignored@example.test"}"""));

        Assert.Null(changes);
        Assert.Equal([new ValidationIssue(string.Empty, "At least one field is required")], issues);
    }

    [Fact]
    public void Profile_field_errors_match_zod()
    {
        Assert.Equal(
            [new ValidationIssue("phone", "Invalid input: expected string, received null")],
            AuthRequest.ReadProfile(Parse("""{"phone":null}""")).Issues);
        Assert.Equal(
            [new ValidationIssue("first_name", "Too small: expected string to have >=1 characters")],
            AuthRequest.ReadProfile(Parse("""{"first_name":" "}""")).Issues);
    }

    [Fact]
    public void Profile_keeps_unsent_fields_null_so_they_are_not_changed()
    {
        var (changes, issues) = AuthRequest.ReadProfile(Parse("""{"phone":" 044 "}"""));

        Assert.Empty(issues);
        Assert.Equal(new ProfileChanges(null, null, "044"), changes);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
