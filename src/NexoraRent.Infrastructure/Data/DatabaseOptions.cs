using MySqlConnector;

namespace NexoraRent.Infrastructure.Data;

/// <summary>
/// MySQL settings. Reads the same DB_* variables as the Express API so both
/// services can share one Railway configuration during the migration.
/// </summary>
public sealed class DatabaseOptions
{
    public string Host { get; set; } = "localhost";
    public uint Port { get; set; } = 3306;
    public string Name { get; set; } = "drenas_rentacar";
    public string User { get; set; } = "root";
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional full connection string. When set, it wins over the individual fields.</summary>
    public string? ConnectionString { get; set; }

    public string BuildConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder(ConnectionString ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            builder.Server = Host;
            builder.Port = Port;
            builder.Database = Name;
            builder.UserID = User;
            builder.Password = Password;
        }

        // CHAR(36) ids stay strings (the client treats them as opaque strings),
        // and DATETIME values are stored in UTC, as on Railway.
        builder.GuidFormat = MySqlGuidFormat.None;
        builder.DateTimeKind = MySqlDateTimeKind.Utc;
        return builder.ConnectionString;
    }
}
