using System.Globalization;
using System.Text.Json;

namespace NexoraRent.Core;

/// <summary>
/// Reads and checks the fields of an admin request body, keeping the first error.
/// <para>
/// Express checks little and lets MySQL reject bad values, which turns them into a 500. The
/// ported endpoints check the same values here and answer 400 with the field's message instead
/// (see "Known differences" in docs/migration-plan.md). After an error, later reads still run but
/// return defaults, so a request reader can read every field and check <see cref="Error"/> once.
/// </para>
/// </summary>
public sealed class BodyReader(JsonElement body)
{
    public string? Error { get; private set; }

    /// <summary>JavaScript truthiness of <c>body[key]</c>, as in <c>if (!body.name)</c>.</summary>
    public bool IsTruthy(string key) =>
        body.TryGetProperty(key, out var value) && value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!.Length > 0,
            JsonValueKind.Number => value.GetDouble() != 0,
            JsonValueKind.True or JsonValueKind.Object or JsonValueKind.Array => true,
            _ => false,
        };

    /// <summary>Whether the key was sent at all, as in <c>if (body.name !== undefined)</c>.</summary>
    public bool Has(string key, out JsonElement value) => body.TryGetProperty(key, out value);

    /// <summary>Text that is not blank and at most <paramref name="maxLength"/> characters.</summary>
    public string? RequiredText(string key, int maxLength) =>
        Value(key) is { ValueKind: JsonValueKind.String } value && value.GetString()!.Trim().Length > 0 && value.GetString()!.Length <= maxLength
            ? value.GetString()
            : Fail<string>($"{key} must be text of 1 to {maxLength} characters");

    /// <summary><c>x || null</c> for a text column: missing, null, false or empty is NULL.</summary>
    public string? OptionalText(string key, int maxLength)
    {
        var value = Value(key);
        if (IsEmpty(value))
        {
            return null;
        }

        return value!.Value.ValueKind == JsonValueKind.String && value.Value.GetString()!.Length <= maxLength
            ? value.Value.GetString()
            : Fail<string>($"{key} must be text of at most {maxLength} characters");
    }

    /// <summary>A value stored as sent: null clears the column, any text (empty too) is kept.</summary>
    public string? NullableText(string key, int maxLength)
    {
        var value = Value(key);
        if (value is null || value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.Value.ValueKind == JsonValueKind.String && value.Value.GetString()!.Length <= maxLength
            ? value.Value.GetString()
            : Fail<string>($"{key} must be text of at most {maxLength} characters");
    }

    /// <summary>One of the values of an <c>ENUM</c> column.</summary>
    public string? OneOf(string key, IReadOnlyCollection<string> allowed) =>
        Value(key) is { ValueKind: JsonValueKind.String } value && allowed.Contains(value.GetString()!)
            ? value.GetString()
            : Fail<string>($"{key} must be one of: {string.Join(", ", allowed)}");

    /// <summary>
    /// A number above 0 and at most <paramref name="max"/>, rounded to cents like a
    /// <c>DECIMAL(n,2)</c> column. A numeric string is accepted too, as MySQL would.
    /// </summary>
    public decimal PositiveDecimal(string key, decimal max)
    {
        var number = Value(key) switch
        {
            { ValueKind: JsonValueKind.Number } value when value.TryGetDecimal(out var d) => d,
            { ValueKind: JsonValueKind.String } value when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => (decimal?)null,
        };

        return number > 0 && number <= max
            ? Math.Round(number.Value, 2, MidpointRounding.AwayFromZero)
            : Fail<decimal>($"{key} must be a number above 0 and at most {max.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>A whole number from <paramref name="min"/> to <paramref name="max"/>; an integer string is accepted too.</summary>
    public int WholeNumber(string key, int min = int.MinValue, int max = int.MaxValue)
    {
        long? number = Value(key) switch
        {
            { ValueKind: JsonValueKind.Number } value when value.TryGetInt64(out var n) => n,
            { ValueKind: JsonValueKind.String } value when long.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };

        if (number is null)
        {
            return Fail<int>($"{key} must be a whole number");
        }

        return number >= min && number <= max
            ? (int)number.Value
            : Fail<int>($"{key} must be from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}");
    }

    public bool Flag(string key) =>
        Value(key) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value
            ? value.ValueKind == JsonValueKind.True
            : Fail<bool>($"{key} must be true or false");

    /// <summary><c>x || null</c> for a <c>DATE</c> column, sent as <c>YYYY-MM-DD</c>.</summary>
    public DateOnly? OptionalDate(string key)
    {
        var value = Value(key);
        if (IsEmpty(value))
        {
            return null;
        }

        return value!.Value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.Value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : Fail<DateOnly?>($"{key} must be a date (YYYY-MM-DD)");
    }

    /// <summary>Records <paramref name="message"/> unless an earlier field already failed.</summary>
    public T? Fail<T>(string message)
    {
        Error ??= message;
        return default;
    }

    private JsonElement? Value(string key) => body.TryGetProperty(key, out var value) ? value : null;

    private static bool IsEmpty(JsonElement? value) =>
        value is null
        || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.False
        || (value.Value.ValueKind == JsonValueKind.String && value.Value.GetString()!.Length == 0);
}
