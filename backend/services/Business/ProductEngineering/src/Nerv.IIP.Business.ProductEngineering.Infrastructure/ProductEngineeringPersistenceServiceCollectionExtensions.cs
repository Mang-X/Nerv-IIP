using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure;

public static class ProductEngineeringPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddProductEngineeringPostgreSqlPersistence(
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
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ProductEngineeringFacts.Schema));

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            options.EnableDetailedErrors();
        });
        services.AddRepositories(typeof(ApplicationDbContext).Assembly);
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<IEngineeringDocumentRepository, EngineeringDocumentRepository>();
        services.AddScoped<IEngineeringItemRepository, EngineeringItemRepository>();
        services.AddScoped<IEngineeringBomRepository, EngineeringBomRepository>();
        services.AddScoped<IManufacturingBomRepository, ManufacturingBomRepository>();
        services.AddScoped<IRoutingRepository, RoutingRepository>();
        services.AddScoped<IStandardOperationRepository, StandardOperationRepository>();
        services.AddScoped<IEngineeringChangeRepository, EngineeringChangeRepository>();
        services.AddScoped<IProductionVersionRepository, ProductionVersionRepository>();
        return services;
    }
}
