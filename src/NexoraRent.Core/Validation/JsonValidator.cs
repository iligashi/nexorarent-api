using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexoraRent.Core.Validation;

public sealed record ValidationIssue(string Field, string Message);

/// <summary>
/// Validates a JSON request body with the rules and messages of the zod 4 schemas
/// in the Express API, so a bad request gets the same 400 body from both APIs.
/// <para>
/// Mirrors zod's behaviour: checks run in declaration order, a type error stops
/// the checks of that field, and object-level refinements are skipped once any
/// type error (or an over-long array) has been found.
/// </para>
/// </summary>
public sealed partial class JsonValidator
{
    private const long MaxSafeInteger = 9007199254740991;
    private readonly List<ValidationIssue> _issues = [];

    public IReadOnlyList<ValidationIssue> Issues => _issues;

    public bool IsValid => _issues.Count == 0;

    /// <summary>True when zod would skip refinements (a type error or an over-long array).</summary>
    public bool Aborted { get; private set; }

    public void AddIssue(string field, string message) => _issues.Add(new ValidationIssue(field, message));

    public string? ReadString(JsonElement parent, string name, StringRules rules, string? path = null)
    {
        path ??= name;
        if (!parent.TryGetProperty(name, out var value))
        {
            if (rules.Required)
            {
                TypeError(path, "string", "undefined");
            }

            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            TypeError(path, "string", TypeName(value));
            return null;
        }

        var text = value.GetString()!;
        if (rules.Trim)
        {
            text = JsTrim(text);
        }

        if (rules.MinLength is { } min && text.Length < min)
        {
            AddIssue(path, $"Too small: expected string to have >={min} characters");
        }

        switch (rules.Format)
        {
            case StringFormat.Uuid when !UuidPattern().IsMatch(text):
                AddIssue(path, "Invalid UUID");
                break;
            case StringFormat.Email when !EmailPattern().IsMatch(text):
                AddIssue(path, "Invalid email address");
                break;
        }

        if (rules.MaxLength is { } max && text.Length > max)
        {
            AddIssue(path, $"Too big: expected string to have <={max} characters");
        }

        if (rules.Refine is { } refine && !refine(text))
        {
            AddIssue(path, rules.RefineMessage ?? "Invalid input");
        }

        return text;
    }

    /// <summary>A whole number in a range, like <c>z.number().int().min(min).max(max)</c>.</summary>
    public int? ReadInteger(JsonElement parent, string name, int min, int max, bool required, string? path = null)
    {
        path ??= name;
        if (!parent.TryGetProperty(name, out var value))
        {
            if (required)
            {
                TypeError(path, "number", "undefined");
            }

            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || double.IsInfinity(number))
        {
            TypeError(path, "number", TypeName(value));
            return null;
        }

        if (Math.Floor(number) != number)
        {
            TypeError(path, "int", "number");
            return null;
        }

        var valid = true;
        if (number > MaxSafeInteger)
        {
            AddIssue(path, $"Too big: expected int to be <={MaxSafeInteger}");
            valid = false;
        }
        else if (number < -MaxSafeInteger)
        {
            AddIssue(path, $"Too small: expected int to be >=-{MaxSafeInteger}");
            valid = false;
        }

        if (number < min)
        {
            AddIssue(path, string.Create(CultureInfo.InvariantCulture, $"Too small: expected number to be >={min}"));
            valid = false;
        }

        if (number > max)
        {
            AddIssue(path, string.Create(CultureInfo.InvariantCulture, $"Too big: expected number to be <={max}"));
            valid = false;
        }

        return valid ? (int)number : null;
    }

    /// <summary>
    /// An optional array of objects with at most <paramref name="maxItems"/> items.
    /// Each object item is passed to <paramref name="readItem"/> in order, with its path
    /// (<c>extras.0</c>). Like zod, an over-long array is reported once and its items are not checked.
    /// </summary>
    public void ReadObjectArray(JsonElement parent, string name, int maxItems, Action<JsonElement, string> readItem)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            TypeError(name, "array", TypeName(value));
            return;
        }

        if (value.GetArrayLength() > maxItems)
        {
            AddIssue(name, $"Too big: expected array to have <={maxItems} items");
            Aborted = true;
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"{name}.{index++}");
            if (item.ValueKind == JsonValueKind.Object)
            {
                readItem(item, path);
            }
            else
            {
                TypeError(path, "object", TypeName(item));
            }
        }
    }

    /// <summary>Raw string values of a property in each array item, for checks across items.</summary>
    public static IEnumerable<string?> RawStrings(JsonElement parent, string arrayName, string property) =>
        parent.TryGetProperty(arrayName, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item =>
                item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null)
            : [];

    /// <summary>The JavaScript type name zod reports in "received ..." messages.</summary>
    public static string TypeName(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => "undefined",
    };

    /// <summary>
    /// <c>String.prototype.trim</c>: .NET white space plus the byte order mark,
    /// which JavaScript also treats as white space.
    /// </summary>
    public static string JsTrim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsJsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    private static bool IsJsWhiteSpace(char c) => char.IsWhiteSpace(c) || c == '﻿';

    private void TypeError(string path, string expected, string received)
    {
        AddIssue(path, $"Invalid input: expected {expected}, received {received}");
        Aborted = true;
    }

    // zod 4's uuid() and email() patterns, anchored with \z so a trailing newline does not match.
    [GeneratedRegex(@"^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-8][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}|00000000-0000-0000-0000-000000000000|ffffffff-ffff-ffff-ffff-ffffffffffff)\z")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"^(?:[A-Za-z0-9_'+\-]+\.)*[A-Za-z0-9_'+\-]*[A-Za-z0-9_+-]@(?:[A-Za-z0-9][A-Za-z0-9\-]*\.)+[A-Za-z]{2,}\z")]
    private static partial Regex EmailPattern();
}

public enum StringFormat
{
    None,
    Uuid,
    Email,
}

public sealed record StringRules
{
    public bool Required { get; init; } = true;
    public bool Trim { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public StringFormat Format { get; init; }
    public Func<string, bool>? Refine { get; init; }
    public string? RefineMessage { get; init; }

    public static readonly StringRules Uuid = new() { Format = StringFormat.Uuid };
}
