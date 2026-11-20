namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>ERP 销售需求演示只允许在 Development 启动。</summary>
public static class ErpDemoSeedStartupGovernance
{
    /// <summary>异常消息里的服务标签；与本服务既有 AutoMigrate 门禁消息一致，不同于 <c>ErpFacts.ServiceName</c>。</summary>
    private const string MessageServiceLabel = "BusinessERP";

    public const string SalesOrderDemandDemoEnabledKey = "Erp:Seed:SalesOrderDemandDemo:Enabled";

    public static bool IsSalesOrderDemandDemoEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetValue(SalesOrderDemandDemoEnabledKey, false);
    }

    /// <summary>非 Development 下任一演示种子开关为真即抛异常；Development 下始终放行。</summary>
    public static void EnsureDevelopmentOnly(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            return;
        }

        if (IsSalesOrderDemandDemoEnabled(configuration))
        {
            throw new InvalidOperationException(
                $"{SalesOrderDemandDemoEnabledKey}=true is only allowed for {MessageServiceLabel} in Development.");
        }
    }
}
