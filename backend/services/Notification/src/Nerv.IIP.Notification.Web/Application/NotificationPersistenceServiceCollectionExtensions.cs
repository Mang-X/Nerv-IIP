using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Notification.Web.Application;

internal static class NotificationPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool usePostgreSql)
    {
        if (usePostgreSql)
        {
            var connectionString = configuration.GetConnectionString("NotificationDb")
                ?? configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:NotificationDb.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification")));
            services.AddScoped<NotificationDatabaseMigrationRunner>();
        }
        else
        {
            var databaseName = configuration["Persistence:InMemoryDatabaseName"] ?? $"notification-{Guid.NewGuid():N}";
            services.AddDbContext<ApplicationDbContext>(options => options
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        }
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<INotificationIntentRepository, NotificationIntentRepository>();
        return services;
    }
}
