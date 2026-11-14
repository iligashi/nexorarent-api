using NexoraRent.Core.Cars;

namespace NexoraRent.UnitTests;

public class CarListQueryTests
{
    [Fact]
    public void Uses_defaults_when_nothing_is_given()
    {
        var query = CarListQuery.FromRaw();

        Assert.Equal(1, query.Page);
        Assert.Equal(12, query.Limit);
        Assert.Equal(0, query.Offset);
        Assert.Equal(CarSort.Newest, query.Sort);
        Assert.Null(query.Category);
        Assert.Null(query.Search);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("abc", 1)]
    [InlineData("3", 3)]
    public void Page_is_at_least_one(string page, int expected)
    {
        Assert.Equal(expected, CarListQuery.FromRaw(page: page).Page);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("50", 50)]
    [InlineData("100000", CarListQuery.MaxLimit)]
    [InlineData("x", CarListQuery.DefaultLimit)]
    public void Limit_is_clamped(string limit, int expected)
    {
        Assert.Equal(expected, CarListQuery.FromRaw(limit: limit).Limit);
    }

    [Fact]
    public void Offset_follows_page_and_limit()
    {
        Assert.Equal(20, CarListQuery.FromRaw(page: "3", limit: "10").Offset);
    }

    [Theory]
    [InlineData("price_asc", CarSort.PriceAsc)]
    [InlineData("price_desc", CarSort.PriceDesc)]
    [InlineData("newest", CarSort.Newest)]
    [InlineData("popular", CarSort.Default)]
    public void Parses_sort(string sort, CarSort expected)
    {
        Assert.Equal(expected, CarListQuery.FromRaw(sort: sort).Sort);
    }

    [Fact]
    public void Blank_filters_are_ignored_and_prices_parsed_invariantly()
    {
        var query = CarListQuery.FromRaw(category: "  ", search: " bmw ", priceMin: "20.5", priceMax: "not-a-number");

        Assert.Null(query.Category);
        Assert.Equal("bmw", query.Search);
        Assert.Equal(20.5m, query.PriceMin);
        Assert.Null(query.PriceMax);
    }
}
