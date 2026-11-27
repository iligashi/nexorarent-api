using NexoraRent.Infrastructure.Users;

namespace NexoraRent.UnitTests;

/// <summary>Hashes must work in both directions between this API and bcryptjs in Express.</summary>
public class PasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    // Made by Express: require('bcryptjs').hashSync('correct horse battery staple', 12) with bcryptjs 2.4.3.
    private const string BcryptjsHash = "$2a$12$PSPRUFzAiMBKknv4s/E7oO6mniEytDIPCmM/hxiNHQt0sdvCdANLC";

    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Verifies_a_hash_made_by_bcryptjs()
    {
        Assert.True(_hasher.Verify(Password, BcryptjsHash));
        Assert.False(_hasher.Verify(Password + "!", BcryptjsHash));
    }

    [Fact]
    public void New_hashes_use_cost_12_and_the_prefix_bcryptjs_writes()
    {
        var hash = _hasher.Hash(Password);

        Assert.StartsWith("$2a$12$", hash, StringComparison.Ordinal);
        Assert.Equal(60, hash.Length);
        Assert.True(_hasher.Verify(Password, hash));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("not-a-hash")]
    [InlineData("")]
    public void A_stored_value_that_is_not_a_hash_never_matches(string stored)
    {
        Assert.False(_hasher.Verify(Password, stored));
    }
}
