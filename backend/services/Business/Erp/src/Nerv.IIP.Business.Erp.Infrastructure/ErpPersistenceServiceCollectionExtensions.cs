using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public static class ErpPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddErpPostgreSqlPersistence(
        this IServiceCollection services,
        string? connectionString,
        bool enableSensitiveDataLogging = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("PostgreSQL persistence requires a connection string.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema));

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            options.EnableDetailedErrors();
        });
        services.AddScoped<IWorkCenterCostRateRevisionLock, PostgreSqlWorkCenterCostRateRevisionLock>();
        services.AddRepositories(typeof(ApplicationDbContext).Assembly);
        services.AddUnitOfWork<ApplicationDbContext>();
        return services;
    }
}
