namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// ERP 演示种子的启动门禁。
///
/// 两个演示种子开关 —— <c>Erp:Seed:SalesOrderDemandDemo:Enabled</c> 与 L1 背景历史
/// <c>LeaderDemo:History:Enabled</c> —— 都会写入虚构的演示数据，只允许在 Development 运行。
/// 当前 Program 把背景历史种子嵌在演示种子块内执行，但这里对两者各自独立判定，
/// 与 Maintenance / IndustrialTelemetry 的双门一致，避免将来拆开嵌套时静默漏门。
/// 非 Development 下开关为真即抛异常拒绝启动（fail-closed），与其余业务服务一致：
/// 宁可起不来，也不允许演示数据污染非开发环境的真实账套。
/// </summary>
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

        if (WorldHistoryConfiguration.IsEnabled(configuration))
        {
            throw new InvalidOperationException(
                $"{WorldHistoryConfiguration.EnabledKey}=true is only allowed for {MessageServiceLabel} in Development.");
        }
    }
}
