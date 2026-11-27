using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexoraRent.Core.Blog;
using NexoraRent.Core.Cars;
using NexoraRent.Core.Catalog;
using NexoraRent.Infrastructure.Blog;
using NexoraRent.Infrastructure.Catalog;
using NexoraRent.Core.Reservations;
using NexoraRent.Core.Loyalty;
using NexoraRent.Core.Notifications;
using NexoraRent.Core.Pricing;
using NexoraRent.Core.Reviews;
using NexoraRent.Core.Users;
using NexoraRent.Infrastructure.Cars;
using NexoraRent.Infrastructure.Data;
using NexoraRent.Infrastructure.Reservations;
using NexoraRent.Infrastructure.Loyalty;
using NexoraRent.Infrastructure.Notifications;
using NexoraRent.Infrastructure.Pricing;
using NexoraRent.Infrastructure.Reviews;
using NexoraRent.Infrastructure.Users;

namespace NexoraRent.Infrastructure;

public static class DependencyInjection
{
    private static int _dapperConfigured;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        if (Interlocked.Exchange(ref _dapperConfigured, 1) == 0)
        {
            // Map snake_case columns (price_per_day) to PascalCase properties (PricePerDay).
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new JsonStringListHandler());
        }

        services.AddSingleton<MySqlConnectionFactory>();
        services.AddScoped<ICarRepository, CarRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IBlogRepository, BlogRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IAdminReviewRepository, AdminReviewRepository>();
        services.AddScoped<IPricingRepository, PricingRepository>();
        services.AddScoped<ILoyaltyRepository, LoyaltyRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationSender, NotificationSender>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
