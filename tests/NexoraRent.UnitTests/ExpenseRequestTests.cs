using System.Text.Json;
using NexoraRent.Core.Expenses;

namespace NexoraRent.UnitTests;

public class ExpenseRequestTests
{
    [Fact]
    public void A_new_expense_keeps_negative_amounts_and_empties_optional_text()
    {
        var (expense, error) = ExpenseRequest.ReadNew(Parse("""{"car_id":"c1","category":"tax","amount":"-20.005","expense_date":"2026-11-02","description":"","vendor":""}"""));

        Assert.Null(error);
        Assert.Equal(new NewCarExpense("c1", "tax", null, -20.01m, new DateOnly(2026, 11, 2), null), expense);
    }

    [Theory]
    [InlineData("{}", ExpenseRequest.RequiredMessage)]
    [InlineData("""{"car_id":"c1","category":"tax","amount":0,"expense_date":"2026-11-02"}""", ExpenseRequest.RequiredMessage)]
    [InlineData("""{"car_id":"c1","category":"food","amount":5,"expense_date":"2026-11-02"}""", "category must be one of")]
    [InlineData("""{"car_id":"c1","category":"tax","amount":"lots","expense_date":"2026-11-02"}""", "amount must be a number other than 0")]
    [InlineData("""{"car_id":"c1","category":"tax","amount":100000000,"expense_date":"2026-11-02"}""", "amount must be a number other than 0")]
    [InlineData("""{"car_id":"c1","category":"tax","amount":5,"expense_date":"02/11/2026"}""", "expense_date must be a date")]
    [InlineData("""{"car_id":7,"category":"tax","amount":5,"expense_date":"2026-11-02"}""", "car_id must be text")]
    public void Bad_expenses_are_rejected(string body, string message)
    {
        var (expense, error) = ExpenseRequest.ReadNew(Parse(body));

        Assert.Null(expense);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_keep_empty_text_and_clear_null_text()
    {
        var (changes, error) = ExpenseRequest.ReadChanges(Parse("""{"description":"","vendor":null,"amount":3}"""));

        Assert.Null(error);
        Assert.Equal(new CarExpenseChanges { Description = "", Vendor = (string?)null, Amount = 3m }, changes);
    }

    [Theory]
    [InlineData("{}", ExpenseRequest.NoFieldsMessage)]
    [InlineData("""{"car_id":null}""", "car_id must be text")]
    [InlineData("""{"amount":null}""", "amount must be a number")]
    [InlineData("""{"expense_date":null}""", "expense_date must be a date")]
    [InlineData("""{"expense_date":""}""", "expense_date must be a date")]
    public void Changes_cannot_clear_required_columns(string body, string message)
    {
        var (changes, error) = ExpenseRequest.ReadChanges(Parse(body));

        Assert.Null(changes);
        Assert.StartsWith(message, error, StringComparison.Ordinal);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
