using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NexoraRent.Core.Blog;
using NexoraRent.Core.Cars;
using NexoraRent.Core.Catalog;
using NexoraRent.Infrastructure.Blog;
using NexoraRent.Infrastructure.Catalog;
using NexoraRent.Core.Users;
using NexoraRent.Infrastructure.Cars;
using NexoraRent.Infrastructure.Data;
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
        return services;
    }
}
