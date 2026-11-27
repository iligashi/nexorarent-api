using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core.Pricing;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Ported from <c>server/src/routes/admin.pricing.routes.js</c>. Staff can read rules, demand and
/// price previews; creating, changing and deleting rules needs a manager or owner.
/// </summary>
internal static class AdminPricingEndpoints
{
    private const string RuleNotFound = "Pricing rule not found";
    private const string CarNotFound = "Car not found";

    public static RouteGroupBuilder MapAdminPricingEndpoints(this RouteGroupBuilder api)
    {
        var pricing = api.MapGroup("/admin/pricing").WithTags("Admin: pricing").RequireAdmin();

        pricing.MapGet("/rules", ListRulesAsync).WithName("AdminListPricingRules");
        pricing.MapPost("/rules", CreateRuleAsync).RequireManager().WithName("AdminCreatePricingRule");
        pricing.MapPut("/rules/{id}", UpdateRuleAsync).RequireManager().WithName("AdminUpdatePricingRule");
        pricing.MapDelete("/rules/{id}", DeleteRuleAsync).RequireManager().WithName("AdminDeletePricingRule");
        pricing.MapGet("/demand", DemandAsync).WithName("AdminPricingDemand");
        pricing.MapGet("/calculate", CalculateAsync).WithName("AdminCalculatePrice");

        return api;
    }

    private static async Task<IResult> ListRulesAsync(IPricingRepository repository, CancellationToken cancellationToken) =>
        Results.Ok(new { rules = await repository.ListRulesAsync(cancellationToken) });

    private static async Task<IResult> CreateRuleAsync(HttpContext context, IPricingRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (rule, error) = PricingRuleRequest.ReadNew(body);
        if (rule is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        var result = await repository.CreateRuleAsync(rule, cancellationToken);
        return result.Status == PricingRuleWriteStatus.CarNotFound
            ? ErrorHandling.Error(StatusCodes.Status400BadRequest, CarNotFound)
            : Results.Json(new { rule = result.Rule }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateRuleAsync(
        string id, HttpContext context, IPricingRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (changes, error) = PricingRuleRequest.ReadChanges(body);
        if (changes is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        var result = await repository.UpdateRuleAsync(id, changes, cancellationToken);
        return result.Status switch
        {
            PricingRuleWriteStatus.CarNotFound => ErrorHandling.Error(StatusCodes.Status400BadRequest, CarNotFound),
            PricingRuleWriteStatus.NotFound => ErrorHandling.Error(StatusCodes.Status404NotFound, RuleNotFound),
            _ => Results.Ok(new { rule = result.Rule }),
        };
    }

    // Express answers 200 whether or not the rule existed; kept so a repeated delete is harmless.
    private static async Task<IResult> DeleteRuleAsync(string id, IPricingRepository repository, CancellationToken cancellationToken)
    {
        await repository.DeleteRuleAsync(id, cancellationToken);
        return Results.Ok(new { message = "Rule deleted" });
    }

    private static async Task<IResult> DemandAsync(
        string? date, IPricingRepository repository, TimeProvider time, CancellationToken cancellationToken)
    {
        // `date || new Date().toISOString().slice(0, 10)`: today in UTC.
        var targetDate = string.IsNullOrEmpty(date) ? time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : date;
        var counts = await repository.CountDemandAsync(targetDate, cancellationToken);

        // `parseInt(count) || 1`: with no available cars Express reports 1, so the percentage stays finite.
        var total = counts.AvailableCars == 0 ? 1 : counts.AvailableCars;
        return Results.Ok(new
        {
            date = targetDate,
            total_cars = total,
            booked_cars = counts.BookedCars,
            demand_percent = (long)Math.Floor(counts.BookedCars / (double)total * 100 + 0.5),
        });
    }

    private static async Task<IResult> CalculateAsync(
        string? car_id, string? pickup_date, string? dropoff_date,
        IPricingRepository repository, TimeProvider time, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(car_id) || string.IsNullOrEmpty(pickup_date) || string.IsNullOrEmpty(dropoff_date))
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, "car_id, pickup_date, dropoff_date required");
        }

        // Express gives NaN days for an unparseable date; here it is a 400.
        if (!DynamicPricing.TryParseDate(pickup_date, out var pickup) || !DynamicPricing.TryParseDate(dropoff_date, out var dropoff))
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, "pickup_date and dropoff_date must be dates");
        }

        var inputs = await repository.LoadPriceInputsAsync(car_id, pickup_date, dropoff_date, cancellationToken);
        return inputs is null
            ? ErrorHandling.Error(StatusCodes.Status404NotFound, CarNotFound)
            : Results.Ok(DynamicPricing.Calculate(inputs, pickup, dropoff, time.GetUtcNow()));
    }
}
