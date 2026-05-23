using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure;

public static class DemandPlanningPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddDemandPlanningPostgreSqlPersistence(
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
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema));

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            options.EnableDetailedErrors();
        });
        services.AddRepositories(typeof(ApplicationDbContext).Assembly);
        services.AddUnitOfWork<ApplicationDbContext>();
        return services;
    }
}
