using NexoraRent.Api.Auth;
using NexoraRent.Api.Http;
using NexoraRent.Core.Loyalty;

namespace NexoraRent.Api.Endpoints;

/// <summary>
/// Ported from <c>server/src/routes/admin.loyalty.routes.js</c>. Staff can read accounts, rewards
/// and the config; adjusting points and changing rewards needs a manager or owner.
/// </summary>
internal static class AdminLoyaltyEndpoints
{
    private const string AccountNotFound = "Account not found";
    private const string RewardNotFound = "Reward not found";

    public static RouteGroupBuilder MapAdminLoyaltyEndpoints(this RouteGroupBuilder api)
    {
        var loyalty = api.MapGroup("/admin/loyalty").WithTags("Admin: loyalty").RequireAdmin();

        loyalty.MapGet("/accounts", ListAccountsAsync).WithName("AdminListLoyaltyAccounts");
        loyalty.MapGet("/accounts/{id}", GetAccountAsync).WithName("AdminGetLoyaltyAccount");
        loyalty.MapPut("/accounts/{id}/adjust", AdjustPointsAsync).RequireManager().WithName("AdminAdjustLoyaltyPoints");
        loyalty.MapGet("/rewards", ListRewardsAsync).WithName("AdminListLoyaltyRewards");
        loyalty.MapPost("/rewards", CreateRewardAsync).RequireManager().WithName("AdminCreateLoyaltyReward");
        loyalty.MapPut("/rewards/{id}", UpdateRewardAsync).RequireManager().WithName("AdminUpdateLoyaltyReward");
        loyalty.MapDelete("/rewards/{id}", DeleteRewardAsync).RequireManager().WithName("AdminDeleteLoyaltyReward");
        loyalty.MapGet("/config", Config).WithName("AdminLoyaltyConfig");

        return api;
    }

    private static async Task<IResult> ListAccountsAsync(ILoyaltyRepository repository, CancellationToken cancellationToken) =>
        Results.Ok(new { accounts = await repository.ListAccountsAsync(cancellationToken) });

    private static async Task<IResult> GetAccountAsync(string id, ILoyaltyRepository repository, CancellationToken cancellationToken) =>
        await repository.GetAccountAsync(id, cancellationToken) is { } detail
            ? Results.Ok(new { account = detail.Account, transactions = detail.Transactions })
            : ErrorHandling.Error(StatusCodes.Status404NotFound, AccountNotFound);

    private static async Task<IResult> AdjustPointsAsync(
        string id, HttpContext context, ILoyaltyRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (adjustment, error) = LoyaltyRequest.ReadAdjustment(body);
        if (adjustment is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        return await repository.AdjustPointsAsync(id, adjustment, cancellationToken) is { } account
            ? Results.Ok(new { account })
            : ErrorHandling.Error(StatusCodes.Status404NotFound, AccountNotFound);
    }

    private static async Task<IResult> ListRewardsAsync(ILoyaltyRepository repository, CancellationToken cancellationToken) =>
        Results.Ok(new { rewards = await repository.ListRewardsAsync(cancellationToken) });

    private static async Task<IResult> CreateRewardAsync(HttpContext context, ILoyaltyRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (reward, error) = LoyaltyRequest.ReadNewReward(body);
        if (reward is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        var created = await repository.CreateRewardAsync(reward, cancellationToken);
        return Results.Json(new { reward = created }, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateRewardAsync(
        string id, HttpContext context, ILoyaltyRepository repository, CancellationToken cancellationToken)
    {
        var (body, bodyError) = await JsonBody.ReadObjectAsync(context.Request, cancellationToken);
        if (bodyError is not null)
        {
            return bodyError;
        }

        var (changes, error) = LoyaltyRequest.ReadRewardChanges(body);
        if (changes is null)
        {
            return ErrorHandling.Error(StatusCodes.Status400BadRequest, error!);
        }

        // Express answers 200 with `{}` for a missing reward; a 404 tells the client it is gone.
        return await repository.UpdateRewardAsync(id, changes, cancellationToken) is { } reward
            ? Results.Ok(new { reward })
            : ErrorHandling.Error(StatusCodes.Status404NotFound, RewardNotFound);
    }

    // Express answers 200 whether or not the reward existed; kept so a repeated delete is harmless.
    private static async Task<IResult> DeleteRewardAsync(string id, ILoyaltyRepository repository, CancellationToken cancellationToken)
    {
        await repository.DeleteRewardAsync(id, cancellationToken);
        return Results.Ok(new { message = "Reward deleted" });
    }

    private static IResult Config() => Results.Ok(new
    {
        config = new
        {
            points_per_euro = 1,
            tier_thresholds = new
            {
                bronze = 0,
                silver = LoyaltyTiers.SilverPoints,
                gold = LoyaltyTiers.GoldPoints,
                platinum = LoyaltyTiers.PlatinumPoints,
            },
        },
    });
}
