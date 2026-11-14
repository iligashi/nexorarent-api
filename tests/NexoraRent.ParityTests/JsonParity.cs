using System.Globalization;
using System.Text.Json;

namespace NexoraRent.ParityTests;

/// <summary>
/// Compares an Express response with a .NET response and lists every
/// difference with its JSON path.
/// <para>
/// Only the documented, intentional differences are accepted:
/// DECIMAL values as strings vs numbers ("25.00" vs 25.00), booleans as 0/1 vs
/// true/false, and timestamps written with or without milliseconds.
/// Anything else, including missing or extra keys, is reported.
/// </para>
/// </summary>
public static class JsonParity
{
    public static IReadOnlyList<string> Compare(JsonElement express, JsonElement dotnet, bool ignoreArrayOrder = false)
    {
        var differences = new List<string>();
        Compare(express, dotnet, "$", ignoreArrayOrder, differences);
        return differences;
    }

    private static void Compare(JsonElement express, JsonElement dotnet, string path, bool ignoreArrayOrder, List<string> differences)
    {
        if (express.ValueKind == JsonValueKind.Object && dotnet.ValueKind == JsonValueKind.Object)
        {
            var expressKeys = express.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var dotnetKeys = dotnet.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var key in expressKeys.Except(dotnetKeys))
            {
                differences.Add($"{path}.{key}: missing in .NET");
            }

            foreach (var key in dotnetKeys.Except(expressKeys))
            {
                differences.Add($"{path}.{key}: only in .NET");
            }

            foreach (var key in expressKeys.Intersect(dotnetKeys))
            {
                Compare(express.GetProperty(key), dotnet.GetProperty(key), $"{path}.{key}", ignoreArrayOrder, differences);
            }

            return;
        }

        if (express.ValueKind == JsonValueKind.Array && dotnet.ValueKind == JsonValueKind.Array)
        {
            var expressItems = express.EnumerateArray().ToList();
            var dotnetItems = dotnet.EnumerateArray().ToList();
            if (expressItems.Count != dotnetItems.Count)
            {
                differences.Add($"{path}: {expressItems.Count} items in Express, {dotnetItems.Count} in .NET");
                return;
            }

            if (ignoreArrayOrder)
            {
                expressItems = [.. expressItems.OrderBy(SortKey, StringComparer.Ordinal)];
                dotnetItems = [.. dotnetItems.OrderBy(SortKey, StringComparer.Ordinal)];
            }

            for (var i = 0; i < expressItems.Count; i++)
            {
                Compare(expressItems[i], dotnetItems[i], $"{path}[{i}]", ignoreArrayOrder, differences);
            }

            return;
        }

        if (!ScalarsMatch(express, dotnet))
        {
            differences.Add($"{path}: Express {express.GetRawText()} vs .NET {dotnet.GetRawText()}");
        }
    }

    private static bool ScalarsMatch(JsonElement express, JsonElement dotnet)
    {
        if (express.ValueKind == dotnet.ValueKind && express.GetRawText() == dotnet.GetRawText())
        {
            return true;
        }

        // DECIMAL columns: mysql2 returns strings, the .NET API returns numbers.
        if (TryNumber(express, out var expressNumber) && TryNumber(dotnet, out var dotnetNumber))
        {
            return expressNumber == dotnetNumber;
        }

        // BOOLEAN columns: mysql2 returns 0/1, the .NET API returns true/false.
        if (dotnet.ValueKind is JsonValueKind.True or JsonValueKind.False && express.ValueKind == JsonValueKind.Number)
        {
            return express.GetRawText() == (dotnet.ValueKind == JsonValueKind.True ? "1" : "0");
        }

        // Timestamps: Express writes milliseconds (.000Z), .NET may not.
        if (express.ValueKind == JsonValueKind.String && dotnet.ValueKind == JsonValueKind.String
            && TryInstant(express.GetString(), out var expressInstant) && TryInstant(dotnet.GetString(), out var dotnetInstant))
        {
            return expressInstant == dotnetInstant;
        }

        return false;
    }

    private static bool TryNumber(JsonElement value, out decimal number)
    {
        number = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out number),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number),
            _ => false,
        };
    }

    private static bool TryInstant(string? value, out DateTimeOffset instant)
    {
        instant = default;
        return value is { Length: >= 20 } && value.EndsWith('Z')
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant);
    }

    private static string SortKey(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) ? id.ToString() : item.GetRawText();
}
