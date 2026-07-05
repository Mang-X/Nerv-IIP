using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Quality.Infrastructure;

public static class QualityPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddQualityPostgreSqlPersistence(
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
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema));

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            options.EnableDetailedErrors();
        });
        services.AddRepositories(typeof(ApplicationDbContext).Assembly);
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<INonconformanceReportRepository, NonconformanceReportRepository>();
        services.AddScoped<IInspectionPlanRepository, InspectionPlanRepository>();
        services.AddScoped<IInspectionRecordRepository, InspectionRecordRepository>();
        services.AddScoped<IInspectionTaskRepository, InspectionTaskRepository>();
        services.AddScoped<IQualityReasonRepository, QualityReasonRepository>();
        services.AddScoped<ICorrectiveActionRepository, CorrectiveActionRepository>();
        return services;
    }
}
