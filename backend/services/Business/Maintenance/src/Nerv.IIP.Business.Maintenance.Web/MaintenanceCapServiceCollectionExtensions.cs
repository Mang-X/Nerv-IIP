using System.Text.Json;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions.CAP;

namespace Nerv.IIP.Business.Maintenance.Web;

/// <summary>
/// Maintenance 的集成事件 / CAP 接线（生产发现边界）。从 <c>Program.cs</c> 抽出以便生产分支可被组合测试直接构建：
/// 非 Testing 分支注册 netcorepal 转换器发布链、CAP outbox、以及 v2 双发 publisher 依赖的
/// <see cref="IMaintenanceIntegrationEventOutboxPublisher"/> 与 <see cref="MaintenanceAssetUnavailableTopicOptions"/>；
/// Testing 分支只注册转换器发现，不接 CAP（与既有 v1 行为一致）。
/// </summary>
public static class MaintenanceCapServiceCollectionExtensions
{
    public static IServiceCollection AddMaintenanceCapIntegrationEvents(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        bool isTesting = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (isTesting)
        {
            services.AddIntegrationEvents(typeof(Program));
            return services;
        }

        services.AddIntegrationEvents(typeof(Program))
            .UseCap<ApplicationDbContext>(b =>
            {
                b.RegisterServicesFromAssemblies(typeof(Program));
                b.AddContextIntegrationFilters();
            });

        services.AddCap(x =>
        {
            x.Version = configuration["Cap:Version"] ?? "v1";
            x.UseEntityFramework<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(configuration, environmentName);
            x.UseDashboard();
        });

        services.AddScoped<IMaintenanceIntegrationEventOutboxPublisher, CapMaintenanceIntegrationEventOutboxPublisher>();
        services.AddSingleton(new MaintenanceAssetUnavailableTopicOptions(environmentName));
        return services;
    }
}
