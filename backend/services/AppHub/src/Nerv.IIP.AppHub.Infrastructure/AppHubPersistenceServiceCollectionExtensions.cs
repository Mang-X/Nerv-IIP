using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Domain;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.AppHub.Infrastructure;

public static class AppHubPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAppHubPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("AppHubDb")
                ?? configuration.GetConnectionString("PostgreSQL")
                ?? throw new InvalidOperationException("PostgreSQL persistence requires ConnectionStrings:AppHubDb.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
            services.AddRepositories(typeof(ApplicationDbContext).Assembly);
            services.AddUnitOfWork<ApplicationDbContext>();
            return services;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAppHubStateStore, InMemoryAppHubStateStore>();
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by AppHub yet.");
    }
}
