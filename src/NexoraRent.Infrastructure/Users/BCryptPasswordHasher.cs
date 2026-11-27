using NexoraRent.Core.Users;
using BCryptNet = BCrypt.Net.BCrypt;

namespace NexoraRent.Infrastructure.Users;

/// <summary>
/// bcrypt with cost 12, like <c>bcrypt.hash(password, 12)</c> in Express. New hashes use the
/// <c>$2a$</c> prefix that bcryptjs 2.x writes, so Express can still check them if the auth
/// routes are ever routed back to it.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    // Made once, on first use, so VerifyNothing costs the same as a real check.
    private static readonly Lazy<string> TimingHash = new(() => BCryptNet.HashPassword(Guid.NewGuid().ToString(), WorkFactor));

    public string Hash(string password) => BCryptNet.HashPassword(password, BCryptNet.GenerateSalt(WorkFactor, 'a'));

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCryptNet.Verify(password, passwordHash);
        }
        catch (Exception exception) when (exception is BCrypt.Net.SaltParseException or ArgumentException)
        {
            // bcryptjs returns false for a stored value that is not a bcrypt hash.
            return false;
        }
    }

    public void VerifyNothing(string password) => BCryptNet.Verify(password, TimingHash.Value);
}
