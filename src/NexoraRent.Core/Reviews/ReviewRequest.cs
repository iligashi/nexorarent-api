using System.Text.Json;
using NexoraRent.Core.Validation;

namespace NexoraRent.Core.Reviews;

/// <summary>Reads a <c>POST /reviews</c> body with the rules of <c>reviewSchema</c> in Express.</summary>
public static class ReviewRequest
{
    public static (NewReview? Review, IReadOnlyList<ValidationIssue> Issues) Read(JsonElement body)
    {
        var v = new JsonValidator();

        var reservationId = v.ReadString(body, "reservation_id", StringRules.Uuid);
        var rating = v.ReadInteger(body, "rating", 1, 5, required: true);
        var comment = v.ReadString(body, "comment", new StringRules { Required = false });

        return v.IsValid
            // Express stores `comment || null`.
            ? (new NewReview(reservationId!, rating!.Value, string.IsNullOrEmpty(comment) ? null : comment), v.Issues)
            : (null, v.Issues);
    }
}
