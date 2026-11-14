using System.Data;
using System.Text.Json;
using Dapper;

namespace NexoraRent.Infrastructure.Data;

/// <summary>
/// Reads JSON array columns (such as <c>cars.features</c>) into a string list.
/// Invalid or empty JSON becomes an empty list, like <c>parseCar</c> in Express.
/// </summary>
internal sealed class JsonStringListHandler : SqlMapper.TypeHandler<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Parse(object value)
    {
        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public override void SetValue(IDbDataParameter parameter, IReadOnlyList<string>? value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = JsonSerializer.Serialize(value ?? []);
    }
}
