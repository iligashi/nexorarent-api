namespace NexoraRent.Core;

/// <summary>
/// One field of a partial update: either absent from the request body or set, possibly to null.
/// Express builds its <c>UPDATE</c> statements from the keys that are present
/// (<c>if (x !== undefined)</c>); this keeps "absent" and "null" apart in the same way.
/// </summary>
public readonly record struct FieldChange<T>(bool IsSet, T Value)
{
    public static implicit operator FieldChange<T>(T value) => new(true, value);
}
