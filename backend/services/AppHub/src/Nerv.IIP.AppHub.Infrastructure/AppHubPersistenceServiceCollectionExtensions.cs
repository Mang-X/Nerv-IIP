using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.AppHub.Infrastructure;

public static class AppHubPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAppHubPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool usePostgreSql)
    {
        if (usePostgreSql)
        {
            var connectionString = configuration.GetConnectionString("AppHubDb")
                ?? configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:AppHubDb.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "apphub")));
            services.AddRepositories(typeof(ApplicationDbContext).Assembly);
            services.AddScoped<IInstanceStateSnapshotRecorder>(provider =>
                provider.GetRequiredService<IApplicationInstanceRepository>());
            services.AddUnitOfWork<ApplicationDbContext>();
            return services;
        }

        services.AddSingleton<InMemoryAppHubStateStore>();
        services.AddSingleton<IAppHubStateStore>(provider => provider.GetRequiredService<InMemoryAppHubStateStore>());
        services.AddSingleton<IInstanceStateSnapshotRecorder>(provider => provider.GetRequiredService<InMemoryAppHubStateStore>());
        return services;
    }
}
