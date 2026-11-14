using System.Text.Json;
using NexoraRent.Core.Validation;

namespace NexoraRent.Core.Reservations;

/// <summary>
/// Reads a <c>POST /reservations</c> body with the rules of <c>reservationSchema</c>
/// (signed-in users) and <c>guestReservationSchema</c> (guests) in the Express API.
/// </summary>
public static class ReservationRequest
{
    public const int MaxExtras = 20;
    private const string DateMessage = "Use a valid local date and time (YYYY-MM-DDTHH:mm:ss)";

    private static readonly StringRules Date = new() { Refine = BookingPeriod.IsValidLocalDateTime, RefineMessage = DateMessage };

    public static (NewReservation? Reservation, IReadOnlyList<ValidationIssue> Issues) Read(JsonElement body, bool isGuest)
    {
        var v = new JsonValidator();

        var carId = v.ReadString(body, "car_id", StringRules.Uuid);
        var pickupLocation = v.ReadString(body, "pickup_location", StringRules.Uuid);
        var dropoffLocation = v.ReadString(body, "dropoff_location", StringRules.Uuid);
        var pickupDate = v.ReadString(body, "pickup_date", Date);
        var dropoffDate = v.ReadString(body, "dropoff_date", Date);
        var guestName = v.ReadString(body, "guest_name", new StringRules { Required = isGuest, Trim = true, MinLength = 1, MaxLength = 200 });
        var guestEmail = v.ReadString(body, "guest_email", new StringRules { Required = isGuest, Trim = true, Format = StringFormat.Email, MaxLength = 255 });
        var guestPhone = v.ReadString(body, "guest_phone", new StringRules { Required = isGuest, Trim = true, MinLength = isGuest ? 8 : null, MaxLength = 30 });
        var notes = v.ReadString(body, "notes", new StringRules { Required = false, MaxLength = 5000 });
        var extras = ReadExtras(v, body);

        // Object-level refinements (validateUniqueExtras in Express). zod skips them after a type error.
        if (!v.Aborted)
        {
            if (BookingPeriod.IsValidLocalDateTime(pickupDate) && BookingPeriod.IsValidLocalDateTime(dropoffDate)
                && !BookingPeriod.TryParse(pickupDate, dropoffDate, out _))
            {
                v.AddIssue("dropoff_date", "Dropoff date must be after pickup date");
            }

            var extraIds = JsonValidator.RawStrings(body, "extras", "extra_id").ToList();
            if (extraIds.Distinct(StringComparer.Ordinal).Count() != extraIds.Count)
            {
                v.AddIssue("extras", "Each extra may only be selected once");
            }
        }

        if (!v.IsValid || !BookingPeriod.TryParse(pickupDate, dropoffDate, out var period))
        {
            return (null, v.Issues);
        }

        return (new NewReservation
        {
            CarId = carId!,
            PickupLocation = pickupLocation!,
            DropoffLocation = dropoffLocation!,
            Period = period!,
            // Express stores `value || null`, so empty strings become NULL.
            GuestName = NullIfEmpty(guestName),
            GuestEmail = NullIfEmpty(guestEmail),
            GuestPhone = NullIfEmpty(guestPhone),
            Notes = NullIfEmpty(notes),
            Extras = extras,
        }, v.Issues);
    }

    private static List<ExtraSelection> ReadExtras(JsonValidator v, JsonElement body)
    {
        var selections = new List<ExtraSelection>();
        v.ReadObjectArray(body, "extras", MaxExtras, (item, path) =>
        {
            var extraId = v.ReadString(item, "extra_id", StringRules.Uuid, $"{path}.extra_id");
            // quantity defaults to 1 when it is left out (zod .default(1)), not when it is null.
            var quantity = item.TryGetProperty("quantity", out _)
                ? v.ReadInteger(item, "quantity", 1, 20, required: true, $"{path}.quantity")
                : 1;

            if (extraId is not null && quantity is not null)
            {
                selections.Add(new ExtraSelection(extraId, quantity.Value));
            }
        });

        return selections;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
