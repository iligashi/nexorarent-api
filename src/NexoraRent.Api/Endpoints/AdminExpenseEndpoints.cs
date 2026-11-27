using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core;
using NexoraRent.Core.Expenses;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Ported from <c>server/src/routes/admin.expenses.routes.js</c>. Staff can read expenses and the
/// summary; recording, changing and deleting expenses needs a manager or owner.
/// </summary>
internal static class AdminExpenseEndpoints
{
    private const int DefaultLimit = 20;
    private const string ExpenseNotFound = "Expense not found";
    private const string CarNotFound = "Car not found";

    public static RouteGroupBuilder MapAdminExpenseEndpoints(this RouteGroupBuilder api)
    {
        var expenses = api.MapGroup("/admin/expenses").WithTags("Admin: expenses").RequireAdmin();

        expenses.MapGet("/", ListAsync).WithName("AdminListExpenses");
        expenses.MapGet("/summary", SummaryAsync).WithName("AdminExpenseSummary");
        expenses.MapGet("/car/{carId}", ListForCarAsync).WithName("AdminListCarExpenses");
        expenses.MapPost("/", CreateAsync).RequireManager().WithName("AdminCreateExpense");
        expenses.MapPut("/{id}", UpdateAsync).RequireManager().WithName("AdminUpdateExpense");
        expenses.MapDelete("/{id}", DeleteAsync).RequireManager().WithName("AdminDeleteExpense");

        return api;
    }

    private static Task<IResult> ListAsync(
        string? car_id, string? category, string? date_from, string? date_to, string? page, string? limit,
        IExpenseRepository repository, CancellationToken cancellationToken) =>
        PageAsync(new ExpenseFilter(Filter(car_id), Filter(category), Filter(date_from), Filter(date_to)), page, limit, repository, cancellationToken);

    private static Task<IResult> ListForCarAsync(
        string carId, string? page, string? limit, IExpenseRepository repository, CancellationToken cancellationToken) =>
        PageAsync(new ExpenseFilter(carId, null, null, null), page, limit, repository, cancellationToken);

    private static async Task<IResult> PageAsync(
        ExpenseFilter filter, string? page, string? limit, IExpenseRepository repository, CancellationToken cancellationToken)
    {
        var paging = Paging.FromRaw(page, limit, DefaultLimit);
        var result = await repository.ListAsync(filter, paging, cancellationToken);
        return Results.Ok(new
        {
            expenses = result.Expenses,
            pagination = new
            {
                page = paging.Page,
                limit = paging.Limit,
                total = result.Total,
                pages = (int)Math.Ceiling(result.Total / (double)paging.Limit),
            },
        });
    }

    private static async Task<IResult> SummaryAsync(IExpenseRepository repository, CancellationToken cancellationToken)
    {
        var summary = await repository.SummaryAsync(cancellationToken);
        return Results.Ok(new { by_category = summary.ByCategory, by_month = summary.ByMonth, profitability = summary.Profitability });
    }

    private static async Task<IResult> CreateAsync(HttpContext context, IExpenseRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (expense, error) = ExpenseRequest.ReadNew(body);
        if (expense is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        var result = await repository.CreateAsync(expense, context.CurrentUser()!.Id, cancellationToken);
        return result.Status == ExpenseWriteStatus.CarNotFound
            ? ErrorHandling.Error(StatusCodes.Status400BadRequest, CarNotFound)
            : Results.Json(new { expense = result.Expense }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(string id, HttpContext context, IExpenseRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        // Express checks that the expense exists before it looks at the body.
        if (!await repository.ExistsAsync(id, cancellationToken))
        {
            return ErrorHandling.Error(StatusCodes.Status404NotFound, ExpenseNotFound);
        }

        var (changes, error) = ExpenseRequest.ReadChanges(body);
        if (changes is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        var result = await repository.UpdateAsync(id, changes, cancellationToken);
        return result.Status switch
        {
            ExpenseWriteStatus.CarNotFound => ErrorHandling.Error(StatusCodes.Status400BadRequest, CarNotFound),
            ExpenseWriteStatus.NotFound => ErrorHandling.Error(StatusCodes.Status404NotFound, ExpenseNotFound),
            _ => Results.Ok(new { expense = result.Expense }),
        };
    }

    private static async Task<IResult> DeleteAsync(string id, IExpenseRepository repository, CancellationToken cancellationToken) =>
        await repository.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { message = "Expense deleted" })
            : ErrorHandling.Error(StatusCodes.Status404NotFound, ExpenseNotFound);

    /// <summary><c>if (x)</c> on a query value: empty means no filter.</summary>
    private static string? Filter(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
