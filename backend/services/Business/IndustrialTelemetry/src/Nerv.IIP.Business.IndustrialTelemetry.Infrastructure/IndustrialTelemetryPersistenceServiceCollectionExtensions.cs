using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

public static class IndustrialTelemetryPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddIndustrialTelemetryPostgreSqlPersistence(
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
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IndustrialTelemetryFacts.Schema));

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
