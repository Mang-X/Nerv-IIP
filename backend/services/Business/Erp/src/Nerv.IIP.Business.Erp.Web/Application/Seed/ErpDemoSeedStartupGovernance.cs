namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// ERP 演示种子的启动门禁。
///
/// 演示种子（<c>Erp:Seed:SalesOrderDemandDemo:Enabled</c> 与其内嵌的 L1 背景历史
/// <c>LeaderDemo:History:Enabled</c>）会写入虚构的演示数据，只允许在 Development 运行。
/// 非 Development 下开关为真即抛异常拒绝启动（fail-closed），与其余业务服务一致：
/// 宁可起不来，也不允许演示数据污染非开发环境的真实账套。
/// </summary>
public static class ErpDemoSeedStartupGovernance
{
    public const string ServiceName = "BusinessERP";

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
                $"'{SalesOrderDemandDemoEnabledKey}'=true is only allowed for {ServiceName} in Development.");
        }

        if (WorldHistoryConfiguration.IsEnabled(configuration))
        {
            throw new InvalidOperationException(
                $"'{WorldHistoryConfiguration.EnabledKey}'=true is only allowed for {ServiceName} in Development.");
        }
    }
}
