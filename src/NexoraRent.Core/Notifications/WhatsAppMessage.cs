using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NexoraRent.Core.Notifications;

/// <summary>The text and link building of <c>sendNotification</c>, kept to the same output.</summary>
public static partial class WhatsAppMessage
{
    /// <summary>
    /// The template values for a reservation, in the order Express sets them. Extra values come
    /// first and the reservation's values replace any with the same key.
    /// </summary>
    public static Dictionary<string, string?> TemplateData(
        IReadOnlyDictionary<string, string?>? extraData, ReservationNotificationData? reservation)
    {
        var data = extraData is null ? [] : new Dictionary<string, string?>(extraData, StringComparer.Ordinal);
        if (reservation is null)
        {
            return data;
        }

        data["customer_name"] = FirstNonEmpty(reservation.CustomerName, reservation.GuestName) ?? "";
        data["reservation_no"] = reservation.ReservationNo;
        data["car_name"] = $"{reservation.Brand} {reservation.Model}";
        data["pickup_date"] = LocaleDate(reservation.PickupDate);
        data["dropoff_date"] = LocaleDate(reservation.DropoffDate);
        data["pickup_location"] = reservation.PickupLocation ?? "";
        data["dropoff_location"] = reservation.DropoffLocation ?? "";
        data["total_price"] = "€" + reservation.TotalPrice.ToString("0.00", CultureInfo.InvariantCulture);
        data["status"] = reservation.Status;
        return data;
    }

    /// <summary><c>renderTemplate</c>: each <c>{{key}}</c> becomes its value, or "" when the value is empty.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> data)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);

        var text = template;
        foreach (var (key, value) in data)
        {
            text = text.Replace("{{" + key + "}}", value ?? "", StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>A wa.me link: the number without spaces, dashes, brackets or a leading +, and the text encoded.</summary>
    public static string Link(string recipient, string message)
    {
        var phone = PhoneNoise().Replace(recipient, "");
        if (phone.StartsWith('+'))
        {
            phone = phone[1..];
        }

        return $"https://wa.me/{phone}?text={EncodeUriComponent(message)}";
    }

    /// <summary>JavaScript's <c>encodeURIComponent</c>, which leaves <c>!*'()</c> as they are.</summary>
    public static string EncodeUriComponent(string value)
    {
        var escaped = Uri.EscapeDataString(value);
        var builder = new StringBuilder(escaped.Length);
        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] == '%' && i + 2 < escaped.Length && Unreserved(escaped.AsSpan(i + 1, 2)) is { } c)
            {
                builder.Append(c);
                i += 2;
            }
            else
            {
                builder.Append(escaped[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// <c>new Date(value).toLocaleDateString()</c> on Node with the default en-US locale, in the
    /// server's time zone (UTC in production, where DATETIME values are read as UTC).
    /// </summary>
    public static string LocaleDate(DateTime value) =>
        value.ToString("M/d/yyyy", CultureInfo.InvariantCulture);

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));

    private static char? Unreserved(ReadOnlySpan<char> hex) => hex switch
    {
        "21" => '!',
        "2A" => '*',
        "27" => '\'',
        "28" => '(',
        "29" => ')',
        _ => null,
    };

    [GeneratedRegex(@"[\s\-\(\)]")]
    private static partial Regex PhoneNoise();
}
