using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.AppHub.Web.Application.IntegrationEvents;

public static class AppHubIntegrationEventDeadLetterServices
{
    public static IServiceCollection AddAppHubIntegrationEventDeadLetterStore(
        this IServiceCollection services,
        bool usePostgreSql)
    {
        if (usePostgreSql)
        {
            services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
        }
        else
        {
            services.AddSingleton<IIntegrationEventDeadLetterStore, InMemoryIntegrationEventDeadLetterStore>();
        }

        return services;
    }
}
