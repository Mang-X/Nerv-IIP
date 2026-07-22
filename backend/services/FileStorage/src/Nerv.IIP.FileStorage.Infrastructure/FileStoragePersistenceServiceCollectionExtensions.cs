using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.FileStorage.Infrastructure;

public static class FileStoragePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddFileStoragePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool usePostgreSql)
    {
        if (!usePostgreSql)
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString("FileStorageDb")
            ?? configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:FileStorageDb.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "filestorage")));
        services.AddScoped<FileStorageDatabaseMigrationRunner>();

        return services;
    }
}
