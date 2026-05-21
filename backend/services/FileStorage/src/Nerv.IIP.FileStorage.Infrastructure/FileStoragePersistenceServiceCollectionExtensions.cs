using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.FileStorage.Infrastructure;

public static class FileStoragePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddFileStoragePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("FileStorageDb")
                ?? configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:FileStorageDb.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "filestorage")));
            services.AddScoped<FileStorageDatabaseMigrationRunner>();
            return services;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by FileStorage yet.");
    }
}
