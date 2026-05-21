using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Ops.Infrastructure;

public static class OpsPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddOpsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
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

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IOpsStateStore, InMemoryOpsStateStore>();
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by Ops yet.");
    }
}
