using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Notification.Web.Application;

internal static class NotificationPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("NotificationDb")
                ?? configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:NotificationDb.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification")));
        }
        else if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            var databaseName = configuration["Persistence:InMemoryDatabaseName"] ?? $"notification-{Guid.NewGuid():N}";
            services.AddDbContext<ApplicationDbContext>(options => options
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        }
        else
        {
            throw new NotSupportedException($"Persistence provider '{provider}' is not supported by Notification.");
        }

        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<INotificationIntentRepository, NotificationIntentRepository>();
        return services;
    }
}
