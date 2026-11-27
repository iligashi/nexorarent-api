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

/// <summary>A user and their stored bcrypt hash, only for checking a password at sign-in.</summary>
public sealed record UserCredentials(User User, string PasswordHash);

/// <summary>A new customer account. <see cref="PasswordHash"/> is already hashed.</summary>
public sealed record NewUser(string Email, string PasswordHash, string FirstName, string LastName, string? Phone);

/// <summary>Profile fields to change. A null field keeps its stored value, like <c>COALESCE</c> in Express.</summary>
public sealed record ProfileChanges(string? FirstName, string? LastName, string? Phone);

public sealed record LoyaltySummary
{
    public required int PointsBalance { get; init; }
    public required string Tier { get; init; }

    /// <summary>What Express returns when the user has no loyalty account yet.</summary>
    public static readonly LoyaltySummary Empty = new() { PointsBalance = 0, Tier = "bronze" };
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<UserCredentials?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Creates a customer account, or returns null when the email is already registered.</summary>
    Task<User?> CreateAsync(NewUser user, CancellationToken cancellationToken);

    Task<User?> UpdateProfileAsync(string id, ProfileChanges changes, CancellationToken cancellationToken);

    Task<LoyaltySummary?> GetLoyaltyAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Hashes and checks passwords with bcrypt, compatible with <c>bcryptjs</c> in Express.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>False for a wrong password and for a stored value that is not a bcrypt hash.</summary>
    bool Verify(string password, string passwordHash);

    /// <summary>
    /// Spends the same time as <see cref="Verify"/> without a stored hash, so a sign-in
    /// for an unknown email cannot be told apart from a wrong password by its timing.
    /// </summary>
    void VerifyNothing(string password);
}
