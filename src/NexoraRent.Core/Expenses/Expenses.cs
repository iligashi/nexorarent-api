using System.Text.Json;

namespace NexoraRent.Core.Expenses;

/// <summary>A row of <c>car_expenses</c>.</summary>
public record CarExpense
{
    public required string Id { get; init; }
    public required string CarId { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }
    public decimal Amount { get; init; }
    public DateTime ExpenseDate { get; init; }
    public string? Vendor { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? CreatedAt { get; init; }
}

/// <summary>An expense in a list, with its car (<c>e.*, c.brand, c.model</c>).</summary>
public sealed record CarExpenseWithCar : CarExpense
{
    public string? Brand { get; init; }
    public string? Model { get; init; }
}

public sealed record CarExpensePage(IReadOnlyList<CarExpenseWithCar> Expenses, int Total);

/// <summary>The list filters; each is applied only when it was sent and not empty.</summary>
public sealed record ExpenseFilter(string? CarId, string? Category, string? DateFrom, string? DateTo);

public sealed record ExpenseCategoryTotal
{
    public required string Category { get; init; }
    public long Count { get; init; }
    public decimal Total { get; init; }
}

public sealed record ExpenseMonthTotal
{
    public required string Month { get; init; }
    public decimal Total { get; init; }
    public long Count { get; init; }
}

public sealed record CarProfitability
{
    public required string CarId { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public decimal Revenue { get; init; }
    public decimal Expenses { get; init; }
    public decimal Profit { get; init; }
}

public sealed record ExpenseSummary(
    IReadOnlyList<ExpenseCategoryTotal> ByCategory,
    IReadOnlyList<ExpenseMonthTotal> ByMonth,
    IReadOnlyList<CarProfitability> Profitability);

public sealed record NewCarExpense(string CarId, string Category, string? Description, decimal Amount, DateOnly ExpenseDate, string? Vendor);

/// <summary>The fields sent to <c>PUT /admin/expenses/:id</c>; each is set only when its key was sent.</summary>
public sealed record CarExpenseChanges
{
    public FieldChange<string> CarId { get; init; }
    public FieldChange<string> Category { get; init; }
    public FieldChange<string?> Description { get; init; }
    public FieldChange<decimal> Amount { get; init; }
    public FieldChange<DateOnly> ExpenseDate { get; init; }
    public FieldChange<string?> Vendor { get; init; }

    public bool IsEmpty => !CarId.IsSet && !Category.IsSet && !Description.IsSet && !Amount.IsSet && !ExpenseDate.IsSet && !Vendor.IsSet;
}

public enum ExpenseWriteStatus
{
    Saved,
    NotFound,
    CarNotFound,
}

public sealed record ExpenseWrite(ExpenseWriteStatus Status, CarExpense? Expense = null);

/// <summary>The categories of the <c>car_expenses.category</c> enum.</summary>
public static class ExpenseCategories
{
    public static readonly IReadOnlyList<string> All = ["insurance", "repair", "fuel", "tire", "wash", "parking", "tax", "registration", "other"];
}

/// <summary>Reads the bodies of the <c>/admin/expenses</c> writes.</summary>
public static class ExpenseRequest
{
    public const string RequiredMessage = "car_id, category, amount, and expense_date are required";
    public const string NoFieldsMessage = "No fields to update";

    private const int MaxIdLength = 36;
    private const int MaxVendorLength = 200;
    private const int MaxDescriptionLength = 65_535;

    public static (NewCarExpense? Expense, string? Error) ReadNew(JsonElement body)
    {
        var reader = new BodyReader(body);

        // `if (!car_id || !category || !amount || !expense_date)`
        if (!reader.IsTruthy("car_id") || !reader.IsTruthy("category") || !reader.IsTruthy("amount") || !reader.IsTruthy("expense_date"))
        {
            return (null, RequiredMessage);
        }

        var carId = reader.RequiredText("car_id", MaxIdLength);
        var category = reader.OneOf("category", ExpenseCategories.All);
        var amount = reader.NonZeroMoney("amount");
        var date = reader.OptionalDate("expense_date");

        // `description || null`, `vendor || null`
        var description = reader.OptionalText("description", MaxDescriptionLength);
        var vendor = reader.OptionalText("vendor", MaxVendorLength);

        return reader.Error is null
            ? (new NewCarExpense(carId!, category!, description, amount, date!.Value, vendor), null)
            : (null, reader.Error);
    }

    public static (CarExpenseChanges? Changes, string? Error) ReadChanges(JsonElement body)
    {
        var reader = new BodyReader(body);
        var changes = new CarExpenseChanges();

        // `if (x !== undefined)`; description and vendor are stored as sent.
        if (reader.Has("car_id", out _)) changes = changes with { CarId = reader.RequiredText("car_id", MaxIdLength)! };
        if (reader.Has("category", out _)) changes = changes with { Category = reader.OneOf("category", ExpenseCategories.All)! };
        if (reader.Has("description", out _)) changes = changes with { Description = reader.NullableText("description", MaxDescriptionLength) };
        if (reader.Has("amount", out _)) changes = changes with { Amount = reader.NonZeroMoney("amount") };
        if (reader.Has("expense_date", out _)) changes = changes with { ExpenseDate = RequiredDate(reader, "expense_date") };
        if (reader.Has("vendor", out _)) changes = changes with { Vendor = reader.NullableText("vendor", MaxVendorLength) };

        if (reader.Error is not null)
        {
            return (null, reader.Error);
        }

        return changes.IsEmpty ? (null, NoFieldsMessage) : (changes, null);
    }

    private static DateOnly RequiredDate(BodyReader reader, string key) =>
        reader.OptionalDate(key) ?? reader.Fail<DateOnly>($"{key} must be a date (YYYY-MM-DD)");
}

/// <summary>Mirrors <c>server/src/routes/admin.expenses.routes.js</c>.</summary>
public interface IExpenseRepository
{
    Task<CarExpensePage> ListAsync(ExpenseFilter filter, Paging paging, CancellationToken cancellationToken);

    Task<ExpenseSummary> SummaryAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken);

    Task<ExpenseWrite> CreateAsync(NewCarExpense expense, string createdBy, CancellationToken cancellationToken);

    Task<ExpenseWrite> UpdateAsync(string id, CarExpenseChanges changes, CancellationToken cancellationToken);

    /// <summary>False when there was no such expense.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}
