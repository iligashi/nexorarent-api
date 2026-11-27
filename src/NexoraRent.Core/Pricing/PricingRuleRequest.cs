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
    private const int MaxIdLength = 36;

    // DECIMAL(4,2) holds up to 99.99; a multiplier of 0 or less would make the car free.
    private const decimal MaxMultiplier = 99.99m;

    public static (NewPricingRule? Rule, string? Error) ReadNew(JsonElement body)
    {
        var reader = new BodyReader(body);

        // `if (!name || !type || !multiplier || !conditions)`
        if (!reader.IsTruthy("name") || !reader.IsTruthy("type") || !reader.IsTruthy("multiplier") || !reader.IsTruthy("conditions"))
        {
            return (null, RequiredMessage);
        }

        var name = reader.RequiredText("name", MaxNameLength);
        var type = reader.OneOf("type", PricingRuleTypes.All);
        var multiplier = reader.PositiveDecimal("multiplier", MaxMultiplier);
        var conditions = Conditions(reader, body.GetProperty("conditions"));

        // `car_id || null`, `category || null`, `priority || 0`, `is_active !== false`, `start_date || null`
        var carId = reader.OptionalText("car_id", MaxIdLength);
        var category = reader.OptionalText("category", MaxCategoryLength);
        var priority = reader.IsTruthy("priority") ? reader.WholeNumber("priority") : 0;
        var isActive = !(body.TryGetProperty("is_active", out var active) && active.ValueKind == JsonValueKind.False);
        var startDate = reader.OptionalDate("start_date");
        var endDate = reader.OptionalDate("end_date");

        return reader.Error is null
            ? (new NewPricingRule(name!, type!, carId, category, multiplier, conditions!, priority, isActive, startDate, endDate), null)
            : (null, reader.Error);
    }

    public static (PricingRuleChanges? Changes, string? Error) ReadChanges(JsonElement body)
    {
        var reader = new BodyReader(body);
        var changes = new PricingRuleChanges();

        // `if (x !== undefined)`: a key that is present is applied, even when it is null.
        if (reader.Has("name", out _)) changes = changes with { Name = reader.RequiredText("name", MaxNameLength)! };
        if (reader.Has("type", out _)) changes = changes with { Type = reader.OneOf("type", PricingRuleTypes.All)! };
        if (reader.Has("car_id", out _)) changes = changes with { CarId = reader.OptionalText("car_id", MaxIdLength) };
        if (reader.Has("category", out _)) changes = changes with { Category = reader.OptionalText("category", MaxCategoryLength) };
        if (reader.Has("multiplier", out _)) changes = changes with { Multiplier = reader.PositiveDecimal("multiplier", MaxMultiplier) };
        if (reader.Has("conditions", out var conditions)) changes = changes with { ConditionsJson = Conditions(reader, conditions)! };
        if (reader.Has("priority", out _)) changes = changes with { Priority = reader.WholeNumber("priority") };
        if (reader.Has("is_active", out _)) changes = changes with { IsActive = reader.Flag("is_active") };
        if (reader.Has("start_date", out _)) changes = changes with { StartDate = reader.OptionalDate("start_date") };
        if (reader.Has("end_date", out _)) changes = changes with { EndDate = reader.OptionalDate("end_date") };

        if (reader.Error is not null)
        {
            return (null, reader.Error);
        }

        return changes.IsEmpty ? (null, NoFieldsMessage) : (changes, null);
    }

    // Stored as JSON text. Express also stores a string (as a JSON string), but such a rule never applies.
    private static string? Conditions(BodyReader reader, JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? value.GetRawText() : reader.Fail<string>("conditions must be an object");
}
