using System.Globalization;
using System.Text.Json;

namespace NexoraRent.Core.Pricing;

/// <summary>
/// Reads the bodies of <c>POST</c> and <c>PUT /admin/pricing/rules</c>.
/// <para>
/// Express checks only that the required fields are truthy and lets MySQL reject the rest, which
/// turns a bad type, multiplier or date into a 500. Here the same values are a 400 with a message
/// (see "Known differences" in docs/migration-plan.md). A body Express accepts is accepted here and
/// stored the same way.
/// </para>
/// </summary>
public static class PricingRuleRequest
{
    public const string RequiredMessage = "name, type, multiplier, and conditions are required";
    public const string NoFieldsMessage = "No fields to update";

    private const int MaxNameLength = 200;
    private const int MaxCategoryLength = 50;

    // DECIMAL(4,2) holds up to 99.99; a multiplier of 0 or less would make the car free.
    private const decimal MaxMultiplier = 99.99m;

    public static (NewPricingRule? Rule, string? Error) ReadNew(JsonElement body)
    {
        // `if (!name || !type || !multiplier || !conditions)`
        if (!IsTruthy(body, "name") || !IsTruthy(body, "type") || !IsTruthy(body, "multiplier") || !IsTruthy(body, "conditions"))
        {
            return (null, RequiredMessage);
        }

        var reader = new Reader(body);
        var name = reader.Name(body.GetProperty("name"));
        var type = reader.Type(body.GetProperty("type"));
        var multiplier = reader.Multiplier(body.GetProperty("multiplier"));
        var conditions = reader.Conditions(body.GetProperty("conditions"));

        // `car_id || null`, `category || null`, `priority || 0`, `is_active !== false`, `start_date || null`
        var carId = reader.OptionalText("car_id", int.MaxValue);
        var category = reader.OptionalText("category", MaxCategoryLength);
        var priority = IsTruthy(body, "priority") ? reader.Priority(body.GetProperty("priority")) : 0;
        var isActive = !(body.TryGetProperty("is_active", out var active) && active.ValueKind == JsonValueKind.False);
        var startDate = reader.OptionalDate("start_date");
        var endDate = reader.OptionalDate("end_date");

        return reader.Error is null
            ? (new NewPricingRule(name!, type!, carId, category, multiplier, conditions!, priority, isActive, startDate, endDate), null)
            : (null, reader.Error);
    }

    public static (PricingRuleChanges? Changes, string? Error) ReadChanges(JsonElement body)
    {
        var reader = new Reader(body);
        var changes = new PricingRuleChanges();

        // `if (x !== undefined)`: a key that is present is applied, even when it is null.
        if (body.TryGetProperty("name", out var name)) changes = changes with { Name = reader.Name(name)! };
        if (body.TryGetProperty("type", out var type)) changes = changes with { Type = reader.Type(type)! };
        if (body.TryGetProperty("car_id", out _)) changes = changes with { CarId = reader.OptionalText("car_id", int.MaxValue) };
        if (body.TryGetProperty("category", out _)) changes = changes with { Category = reader.OptionalText("category", MaxCategoryLength) };
        if (body.TryGetProperty("multiplier", out var multiplier)) changes = changes with { Multiplier = reader.Multiplier(multiplier) };
        if (body.TryGetProperty("conditions", out var conditions)) changes = changes with { ConditionsJson = reader.Conditions(conditions)! };
        if (body.TryGetProperty("priority", out var priority)) changes = changes with { Priority = reader.Priority(priority) };
        if (body.TryGetProperty("is_active", out var isActive)) changes = changes with { IsActive = reader.Flag(isActive) };
        if (body.TryGetProperty("start_date", out _)) changes = changes with { StartDate = reader.OptionalDate("start_date") };
        if (body.TryGetProperty("end_date", out _)) changes = changes with { EndDate = reader.OptionalDate("end_date") };

        if (reader.Error is not null)
        {
            return (null, reader.Error);
        }

        return changes.IsEmpty ? (null, NoFieldsMessage) : (changes, null);
    }

    private static bool IsTruthy(JsonElement body, string key) =>
        body.TryGetProperty(key, out var value) && value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!.Length > 0,
            JsonValueKind.Number => value.GetDouble() != 0,
            JsonValueKind.True or JsonValueKind.Object or JsonValueKind.Array => true,
            _ => false,
        };

    /// <summary>Collects the first error; later reads still run but return defaults.</summary>
    private sealed class Reader(JsonElement body)
    {
        public string? Error { get; private set; }

        public string? Name(JsonElement value) =>
            value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length is > 0 and <= MaxNameLength
                ? value.GetString()
                : Fail<string>($"name must be text of 1 to {MaxNameLength} characters");

        public string? Type(JsonElement value) =>
            value.ValueKind == JsonValueKind.String && PricingRuleTypes.All.Contains(value.GetString()!)
                ? value.GetString()
                : Fail<string>($"type must be one of: {string.Join(", ", PricingRuleTypes.All)}");

        public decimal Multiplier(JsonElement value)
        {
            // The admin page sends a number; a numeric string is accepted too, as MySQL would.
            var number = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var d) => d,
                JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
                _ => (decimal?)null,
            };

            return number is > 0 and <= MaxMultiplier
                ? Math.Round(number.Value, 2, MidpointRounding.AwayFromZero)
                : Fail<decimal>($"multiplier must be a number above 0 and at most {MaxMultiplier.ToString(CultureInfo.InvariantCulture)}");
        }

        public string? Conditions(JsonElement value) =>
            value.ValueKind == JsonValueKind.Object
                ? value.GetRawText()
                : Fail<string>("conditions must be an object");

        public int Priority(JsonElement value) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var priority)
                ? priority
                : Fail<int>("priority must be a whole number");

        public bool Flag(JsonElement value) =>
            value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.ValueKind == JsonValueKind.True
                : Fail<bool>("is_active must be true or false");

        /// <summary><c>x || null</c> for a text column: empty or missing is NULL.</summary>
        public string? OptionalText(string key, int maxLength)
        {
            if (!body.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.False
                || (value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 0))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= maxLength
                ? value.GetString()
                : Fail<string>($"{key} must be text of at most {maxLength} characters");
        }

        /// <summary><c>x || null</c> for a DATE column, sent as <c>YYYY-MM-DD</c>.</summary>
        public DateOnly? OptionalDate(string key)
        {
            if (!body.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.False
                || (value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 0))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : Fail<DateOnly?>($"{key} must be a date (YYYY-MM-DD)");
        }

        private T? Fail<T>(string message)
        {
            Error ??= message;
            return default;
        }
    }
}
