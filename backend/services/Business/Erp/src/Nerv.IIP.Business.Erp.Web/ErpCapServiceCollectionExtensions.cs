using System.Text.Json;
using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedTransactions.CAP;

namespace Nerv.IIP.Business.Erp.Web;

/// <summary>
/// ERP 的 CAP 集成事件接线入口。
///
/// 存在的理由（#3179）：这段接线原本内联在 <c>Program.cs</c> 的 <c>else (!isTesting)</c> 分支里，
/// <c>WebApplicationFactory</c>（Testing 环境）走不到，因此「本服务是否安装了共享 canonical topic
/// selector」这条不变量在默认 CI 上没有任何可执行的观察点。把它提成命名入口后，
/// 普通单测可以按生产参数把容器建起来、解析 CAP 的 consumer selector 接口
/// 并断言拿到的是共享实现——与 MES 的 <c>AddMesCapIntegrationEvents</c> 同一姿势。
///
/// 本类型只做搬运：注册项、注册顺序与传入参数与内联时逐行一致，不改变生产接线语义。
/// </summary>
public static class ErpCapServiceCollectionExtensions
{
    public static IServiceCollection AddErpCapIntegrationEvents(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        bool isTesting = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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
            x.UseConfiguredRecovery(configuration);
            x.UseEntityFramework<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(configuration, environmentName);
            x.UseDashboard();
        });
        services.AddNervIipCanonicalTopicSubscriptions(environmentName);

        return services;
    }
}
