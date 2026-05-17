using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Iam.Infrastructure;

public static class IamPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddIamPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("IamDb")
                ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:IamDb.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam")));
            services.AddRepositories(typeof(ApplicationDbContext).Assembly);
            services.AddUnitOfWork<ApplicationDbContext>();
            services.AddScoped<IamDatabaseMigrationRunner>();
            return services;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<InMemoryIamStore>();
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by IAM.");
    }
}
