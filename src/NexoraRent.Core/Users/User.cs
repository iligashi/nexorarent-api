namespace NexoraRent.Core.Users;

public sealed record User
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Phone { get; init; }
    public required string Role { get; init; }
}

public static class Roles
{
    /// <summary>Roles allowed into the admin area, matching <c>requireAdmin</c> in Express.</summary>
    public static readonly IReadOnlyList<string> Admin = ["staff", "manager", "owner"];
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
