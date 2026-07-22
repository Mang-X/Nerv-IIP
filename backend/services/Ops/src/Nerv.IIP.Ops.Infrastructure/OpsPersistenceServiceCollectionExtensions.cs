using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Ops.Infrastructure;

public static class OpsPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddOpsPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool usePostgreSql)
    {
        if (usePostgreSql)
        {
            var connectionString = configuration.GetConnectionString("OpsDb")
                ?? throw new InvalidOperationException("Connection string 'OpsDb' is required when Ops uses PostgreSQL persistence.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ops")));
            services.AddRepositories(typeof(ApplicationDbContext).Assembly);
            services.AddUnitOfWork<ApplicationDbContext>();
            services.AddScoped<IOperationTaskRepository, OperationTaskRepository>();
            services.AddScoped<IOperationTemplateRepository, OperationTemplateRepository>();
            return services;
        }

        services.AddSingleton<IOpsStateStore, InMemoryOpsStateStore>();
        return services;
    }
}
