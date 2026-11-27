using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NexoraRent.Api.Http;

namespace NexoraRent.Api.Auth;

/// <summary>
/// The HS256 keys shared with Express: JWT_SECRET signs access tokens and
/// JWT_REFRESH_SECRET signs refresh tokens. In production both are required and
/// checked with the rules of <c>validateEnvironment</c> in Express.
/// </summary>
internal sealed partial class JwtSecrets
{
    private const int MinimumLength = 32;

    private JwtSecrets(byte[] access, byte[] refresh)
    {
        Access = access;
        Refresh = refresh;
    }

    public byte[] Access { get; }

    public byte[] Refresh { get; }

    public static JwtSecrets Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var access = LegacyEnvironment.JwtSecret(configuration);
        var refresh = LegacyEnvironment.JwtRefreshSecret(configuration);

        if (environment.IsProduction())
        {
            var errors = new List<string>();
            Check("JWT_SECRET", access, errors);
            Check("JWT_REFRESH_SECRET", refresh, errors);
            if (!string.IsNullOrEmpty(access) && access == refresh)
            {
                errors.Add("JWT_SECRET and JWT_REFRESH_SECRET must be different");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Invalid JWT configuration: {string.Join("; ", errors)}.");
            }
        }

        // Outside production a missing secret gets a random one: the API starts, but tokens
        // from Express (or from before a restart) will not validate.
        return new JwtSecrets(KeyOrRandom(access), KeyOrRandom(refresh));
    }

    private static void Check(string name, string? value, List<string> errors)
    {
        if (string.IsNullOrEmpty(value) || value.Length < MinimumLength)
        {
            errors.Add($"{name} must be at least {MinimumLength} characters");
        }
        else if (Placeholder().IsMatch(value))
        {
            errors.Add($"{name} must not be a placeholder");
        }
    }

    private static byte[] KeyOrRandom(string? secret) =>
        string.IsNullOrEmpty(secret) || secret.Length < MinimumLength
            ? RandomNumberGenerator.GetBytes(48)
            : Encoding.UTF8.GetBytes(secret);

    [GeneratedRegex("change.?me|your_|example|placeholder", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();
}
