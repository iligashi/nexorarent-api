using Dapper;
using MySqlConnector;
using NexoraRent.Core;
using NexoraRent.Core.Expenses;
using NexoraRent.Infrastructure.Data;

namespace NexoraRent.Infrastructure.Expenses;

/// <summary>Mirrors <c>server/src/routes/admin.expenses.routes.js</c> in the Express API.</summary>
public sealed class ExpenseRepository(MySqlConnectionFactory connections) : IExpenseRepository
{
    private const string ExpenseColumns = "e.id, e.car_id, e.category, e.description, e.amount, e.expense_date, e.vendor, e.created_by, e.created_at";

    public async Task<CarExpensePage> ListAsync(ExpenseFilter filter, Paging paging, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(paging);

        // Fixed conditions; only the values are parameters. Dates are compared as sent, like Express.
        var conditions = new List<string>();
        if (filter.CarId is not null) conditions.Add("e.car_id = @CarId");
        if (filter.Category is not null) conditions.Add("e.category = @Category");
        if (filter.DateFrom is not null) conditions.Add("e.expense_date >= @DateFrom");
        if (filter.DateTo is not null) conditions.Add("e.expense_date <= @DateTo");
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var parameters = new { filter.CarId, filter.Category, filter.DateFrom, filter.DateTo, paging.Limit, paging.Offset };

        await using var connection = await connections.OpenAsync(cancellationToken);

        // The id breaks ties between expenses on the same day, so pages do not overlap.
        var rows = await connection.QueryAsync<CarExpenseWithCar>(new CommandDefinition(
            $"""
            SELECT {ExpenseColumns}, c.brand, c.model
            FROM car_expenses e
            LEFT JOIN cars c ON c.id = e.car_id
            {where}
            ORDER BY e.expense_date DESC, e.id
            LIMIT @Limit OFFSET @Offset
            """,
            parameters,
            cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM car_expenses e {where}", parameters, cancellationToken: cancellationToken));

        return new CarExpensePage([.. rows], total);
    }

    public async Task<ExpenseSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Ties are broken by name or id so the order is the same on every call.
        var byCategory = await connection.QueryAsync<ExpenseCategoryTotal>(new CommandDefinition(
            """
            SELECT category, COUNT(*) AS count, SUM(amount) AS total
            FROM car_expenses
            GROUP BY category
            ORDER BY total DESC, category
            """,
            cancellationToken: cancellationToken));
        var byMonth = await connection.QueryAsync<ExpenseMonthTotal>(new CommandDefinition(
            """
            SELECT DATE_FORMAT(expense_date, '%Y-%m') AS month, SUM(amount) AS total, COUNT(*) AS count
            FROM car_expenses
            WHERE expense_date >= NOW() - INTERVAL 12 MONTH
            GROUP BY month
            ORDER BY month
            """,
            cancellationToken: cancellationToken));
        var profitability = await connection.QueryAsync<CarProfitability>(new CommandDefinition(
            """
            SELECT c.id AS car_id, c.brand, c.model,
                   COALESCE(rev.revenue, 0) AS revenue,
                   COALESCE(exp.expenses, 0) AS expenses,
                   COALESCE(rev.revenue, 0) - COALESCE(exp.expenses, 0) AS profit
            FROM cars c
            LEFT JOIN (
              SELECT car_id, SUM(total_price) AS revenue
              FROM reservations
              WHERE status IN ('confirmed', 'completed', 'active')
              GROUP BY car_id
            ) rev ON rev.car_id = c.id
            LEFT JOIN (
              SELECT car_id, SUM(amount) AS expenses
              FROM car_expenses
              GROUP BY car_id
            ) exp ON exp.car_id = c.id
            ORDER BY profit DESC, c.id
            """,
            cancellationToken: cancellationToken));

        return new ExpenseSummary([.. byCategory], [.. byMonth], [.. profitability]);
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM car_expenses WHERE id = @id", new { id }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<ExpenseWrite> CreateAsync(NewCarExpense expense, string createdBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expense);
        var id = Guid.NewGuid().ToString();

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO car_expenses (id, car_id, category, description, amount, expense_date, vendor, created_by)
                VALUES (@id, @CarId, @Category, @Description, @Amount, @ExpenseDate, @Vendor, @createdBy)
                """,
                new
                {
                    id,
                    expense.CarId,
                    expense.Category,
                    expense.Description,
                    expense.Amount,
                    ExpenseDate = ToDate(expense.ExpenseDate),
                    expense.Vendor,
                    createdBy,
                },
                cancellationToken: cancellationToken));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            return new ExpenseWrite(ExpenseWriteStatus.CarNotFound);
        }

        return new ExpenseWrite(ExpenseWriteStatus.Saved, await GetAsync(connection, id, cancellationToken));
    }

    public async Task<ExpenseWrite> UpdateAsync(string id, CarExpenseChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Each column comes from a fixed list; only the values are parameters.
        var sets = new List<string>();
        var parameters = new DynamicParameters(new { id });
        void Set<T>(FieldChange<T> field, string column, Func<T, object?>? convert = null)
        {
            if (field.IsSet)
            {
                sets.Add($"{column} = @{column}");
                parameters.Add(column, convert is null ? field.Value : convert(field.Value));
            }
        }

        Set(changes.CarId, "car_id");
        Set(changes.Category, "category");
        Set(changes.Description, "description");
        Set(changes.Amount, "amount");
        Set(changes.ExpenseDate, "expense_date", date => ToDate(date));
        Set(changes.Vendor, "vendor");

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE car_expenses SET {string.Join(", ", sets)} WHERE id = @id", parameters, cancellationToken: cancellationToken));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            return new ExpenseWrite(ExpenseWriteStatus.CarNotFound);
        }

        // The row can be deleted between the existence check and the update.
        var expense = await GetAsync(connection, id, cancellationToken);
        return expense is null ? new ExpenseWrite(ExpenseWriteStatus.NotFound) : new ExpenseWrite(ExpenseWriteStatus.Saved, expense);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM car_expenses WHERE id = @id", new { id }, cancellationToken: cancellationToken)) > 0;
    }

    private static Task<CarExpense?> GetAsync(MySqlConnection connection, string id, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<CarExpense>(new CommandDefinition(
            $"SELECT {ExpenseColumns} FROM car_expenses e WHERE e.id = @id", new { id }, cancellationToken: cancellationToken));

    private static DateTime ToDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
