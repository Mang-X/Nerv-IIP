using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotNetCore.CAP.Internal;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.Primitives;
using System.Text.Json;

namespace Nerv.IIP.Business.Mes.Web;

public static class MesCapServiceCollectionExtensions
{
    public static IServiceCollection AddMesCapIntegrationEvents(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        bool isTesting = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddContext().AddEnvContext().AddCapContextProcessor();
        services.AddScoped<IntegrationEventCapFailureDeadLetterer>();

        if (isTesting)
        {
            services.AddIntegrationEvents(typeof(Program));
            services.AddCap(options =>
            {
                options.Version = configuration["Cap:Version"] ?? "v1";
                options.JsonSerializerOptions.AddNetCorePalJsonConverters();
            });
            return services;
        }

        services.AddIntegrationEvents(typeof(Program))
            .UseCap<ApplicationDbContext>(builder =>
            {
                builder.RegisterServicesFromAssemblies(typeof(Program));
                builder.AddContextIntegrationFilters();
            });

        services.AddCap(options =>
        {
            options.Version = configuration["Cap:Version"] ?? "v1";
            options.UseEntityFramework<ApplicationDbContext>();
            options.JsonSerializerOptions.AddNetCorePalJsonConverters();
            options.UseConfiguredTransport(configuration, environmentName);
            options.UseIntegrationEventDeadLetterOnFailedThreshold();
            options.UseDashboard();
        });
        services.Replace(ServiceDescriptor.Singleton<IConsumerServiceSelector>(serviceProvider =>
            new MesDeploymentProfileConsumerServiceSelector(serviceProvider, environmentName)));

        return services;
    }
}
