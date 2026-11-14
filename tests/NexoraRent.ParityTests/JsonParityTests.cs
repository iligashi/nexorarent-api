using System.Text.Json;

namespace NexoraRent.ParityTests;

/// <summary>Checks the comparer itself, so a parity pass means something.</summary>
public class JsonParityTests
{
    [Fact]
    public void Accepts_the_documented_representation_differences()
    {
        var express = Parse("""{"price":"25.00","on":1,"off":0,"at":"2026-03-28T10:00:00.000Z","n":null}""");
        var dotnet = Parse("""{"price":25.0,"on":true,"off":false,"at":"2026-03-28T10:00:00Z","n":null}""");

        Assert.Empty(JsonParity.Compare(express, dotnet));
    }

    [Theory]
    [InlineData("""{"a":1}""", """{"a":2}""", "$.a")]
    [InlineData("""{"a":"x"}""", """{"a":"y"}""", "$.a")]
    [InlineData("""{"a":1,"b":2}""", """{"a":1}""", "$.b: missing in .NET")]
    [InlineData("""{"a":1}""", """{"a":1,"b":2}""", "$.b: only in .NET")]
    [InlineData("""{"on":1}""", """{"on":false}""", "$.on")]
    [InlineData("""{"list":[1,2]}""", """{"list":[1]}""", "$.list: 2 items")]
    [InlineData("""{"v":null}""", """{"v":0}""", "$.v")]
    [InlineData("""{"at":"2026-03-28T10:00:00.000Z"}""", """{"at":"2026-03-28T11:00:00Z"}""", "$.at")]
    public void Reports_real_differences(string express, string dotnet, string expected)
    {
        var differences = JsonParity.Compare(Parse(express), Parse(dotnet));

        Assert.Contains(differences, d => d.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Array_order_matters_unless_ignored()
    {
        var express = Parse("""[{"id":"a"},{"id":"b"}]""");
        var dotnet = Parse("""[{"id":"b"},{"id":"a"}]""");

        Assert.NotEmpty(JsonParity.Compare(express, dotnet));
        Assert.Empty(JsonParity.Compare(express, dotnet, ignoreArrayOrder: true));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
