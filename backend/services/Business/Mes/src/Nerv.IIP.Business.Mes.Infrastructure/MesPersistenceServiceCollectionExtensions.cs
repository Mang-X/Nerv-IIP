using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public static class MesPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddMesPostgreSqlPersistence(
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
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema));

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            options.EnableDetailedErrors();
        });
        services.AddRepositories(typeof(ApplicationDbContext).Assembly);
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
        services.AddScoped<IOperationTaskRepository, OperationTaskRepository>();
        services.AddScoped<IProductionReportRepository, ProductionReportRepository>();
        return services;
    }
}
