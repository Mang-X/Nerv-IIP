using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace Nerv.IIP.Business.Quality.Web.Application.Scheduling;

/// <param name="Operations">当前卡在「有报工事实、无发布事实」且已超过自愈窗口的工序行数。</param>
/// <param name="OldestAge">这些行里最早那一条报工事实距今的时长；无卡住行时为 <see cref="TimeSpan.Zero"/>。</param>
public sealed record WorkOrderReleaseFactBacklogReading(int Operations, TimeSpan OldestAge);

/// <summary>
/// 「工单发布事实缺失」的存量读数（#2983）。
///
/// 稳态**不是**「<c>PeriodicInspectionOperations</c> 里没有行」：同一封
/// <c>ProductionReportRecordedIntegrationEvent</c> 还有第二个消费组
/// （<see cref="IntegrationEventHandlers.ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection"/>），
/// 它取不到行就 <c>CreatePending</c> 建一行。所以准确表达是「**有报工事实、无发布事实**」，
/// 而两侧都住在 Quality 自己的库里，一条反连即可判定，不需要跨服务对账。
///
/// 判据与首件读面同源：<c>GetFirstArticleConfirmationQuery.ResolveMissingTaskStatusAsync</c> 按
/// <c>SkuCode</c> 为空判 <c>not-synchronized</c>，而
/// <c>ck_periodic_inspection_operations_release_snapshot</c> 把 <c>sku_code</c> 与
/// <c>released_at_utc</c> 钉成同生同灭，因此这里用 <c>ReleasedAtUtc == null</c> 表达同一个状态。
/// </summary>
public sealed class WorkOrderReleaseFactBacklogMetrics(CollectorRegistry registry)
{
    private readonly Gauge backlogOperations = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_quality_release_fact_backlog_operations",
        "Quality operations that carry MES production-report facts but still have no work-order release facts after the self-healing window.",
        new GaugeConfiguration { LabelNames = ["organization", "environment"] });

    private readonly Gauge oldestBacklogAgeSeconds = Metrics.WithCustomRegistry(registry).CreateGauge(
        "nerv_iip_quality_release_fact_backlog_oldest_age_seconds",
        "Age in seconds of the earliest production-report fact among Quality operations still missing work-order release facts.",
        new GaugeConfiguration { LabelNames = ["organization", "environment"] });

    /// <summary>
    /// 读一次并把两个 Gauge 写成该 scope 的当前值。**每个已配置 scope 每轮都写**（包括写 0），
    /// 否则回填跑完后那条 label 会停在最后一次非零读数上，指标就答不了「跑成没跑成」。
    /// </summary>
    public async Task<WorkOrderReleaseFactBacklogReading> RefreshAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        DateTime nowUtc,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var reading = await ReadAsync(dbContext, organizationId, environmentId, nowUtc, staleAfter, cancellationToken);
        backlogOperations.WithLabels(organizationId, environmentId).Set(reading.Operations);
        oldestBacklogAgeSeconds.WithLabels(organizationId, environmentId).Set(reading.OldestAge.TotalSeconds);
        return reading;
    }

    /// <summary>
    /// <paramref name="staleAfter"/> 是**年龄下限**，不是告警阈值：低于它的行仍可能自行恢复，
    /// 计进来会让 Gauge 在正常流量下抖出非零值，指标随之失去可读性。
    ///
    /// 「有报工事实」与「已过自愈窗口」合成同一个谓词
    /// （<c>ProductionReports.Any(报工时刻 &lt;= 下限)</c>）而不是拆成
    /// <c>Count &gt; 0 &amp;&amp; Min(...) &lt;= 下限</c>：后者在 SQL 里 <c>Count &gt; 0</c> 是死条件——
    /// 空集合的 <c>MIN</c> 返回 <c>NULL</c>，比较结果 unknown，行本来就会被滤掉，
    /// 于是那一支既不改变结果也杀不掉任何变异。
    ///
    /// 年龄取该工序**最早**的报工事实：卡住的起点是第一次报工，不是最近一次。
    /// 冲销报工也算「报工事实」——它的存在本身就意味着此前发生过一次正向报工。
    /// </summary>
    internal static async Task<WorkOrderReleaseFactBacklogReading> ReadAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        DateTime nowUtc,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var staleBefore = nowUtc - staleAfter;
        var backlog = dbContext.PeriodicInspectionOperations
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.ReleasedAtUtc == null
                && x.ProductionReports.Any(report => report.ReportedAtUtc <= staleBefore));
        var operations = await backlog.CountAsync(cancellationToken);
        if (operations == 0)
        {
            return new WorkOrderReleaseFactBacklogReading(0, TimeSpan.Zero);
        }

        var earliestReportedAtUtc = await backlog
            .MinAsync(x => (DateTime?)x.ProductionReports.Min(report => report.ReportedAtUtc), cancellationToken);
        return new WorkOrderReleaseFactBacklogReading(
            operations,
            earliestReportedAtUtc.HasValue ? nowUtc - earliestReportedAtUtc.Value : TimeSpan.Zero);
    }
}

/// <summary>
/// 「工单发布事实缺失」巡检（#2983）。恢复路径由 #3000 的
/// <c>POST /internal/business-mes/v1/work-order-release-projection-backfill</c> 提供，本巡检只回答
/// 「**现在还有几道工序卡着、卡了多久**」——即「该不该去跑」与「跑成没跑成」。
///
/// 姿势与 Quality 已有的三个 <see cref="BackgroundService"/> 一致：配置开关 + <see cref="PeriodicTimer"/>
/// + 按 org/env scope 遍历；开关关闭时直接返回，连指标对象都不构造（因而 <c>/metrics</c> 上不会出现这两个族）。
/// </summary>
public sealed class WorkOrderReleaseFactBacklogScanner(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<WorkOrderReleaseFactBacklogScanner> logger,
    TimeProvider timeProvider)
    : BackgroundService
{
    internal const string EnabledConfigurationKey = "Quality:ReleaseFactBacklog:Enabled";
    internal const string IntervalConfigurationKey = "Quality:ReleaseFactBacklog:Interval";
    internal const string StaleAfterConfigurationKey = "Quality:ReleaseFactBacklog:StaleAfter";
    internal const string ScopesConfigurationKey = "Quality:ReleaseFactBacklog:Scopes";
    internal const string OrganizationIdConfigurationKey = "Quality:ReleaseFactBacklog:OrganizationId";
    internal const string EnvironmentIdConfigurationKey = "Quality:ReleaseFactBacklog:EnvironmentId";

    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 年龄下限的默认值。取值依据是**该状态还能自行恢复多久**：投影缺失只有一条不需要人介入的恢复通道——
    /// CAP 对 <c>mes.WorkOrderReleased</c> 的重投。业务事实非法那一支由
    /// <c>IntegrationEventConsumerGuard</c> 写死信后正常返回、不抛异常，因此进入 CAP 重试循环的只剩基础设施故障，
    /// 其自愈预算恰为 <c>FailedRetryCount × FailedRetryInterval</c>。本仓两者都不覆盖
    /// （<c>Cap:FailedRetryInterval</c> 在所有 appsettings 与 AppHost 里均未设置，<c>FailedRetryCount</c> 连配置键都没有），
    /// 所以适用的是 CAP 自身的默认预算——实测 <c>FailedRetryCount=50</c>、<c>FailedRetryInterval=60</c>，
    /// 即 50 分钟；默认下限取其上的整点余量 1 小时。
    /// 这条耦合由 <c>WorkOrderReleaseFactBacklogScannerTests</c> 按 <c>CapOptions</c> 实际默认值机检，
    /// CAP 改默认值时会红，届时须重算而不是改断言。
    /// </summary>
    internal static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>(EnabledConfigurationKey))
        {
            return;
        }

        var scopes = GetConfiguredScopes().Distinct().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("Quality work-order release fact backlog scan is enabled but no organization/environment scope is configured.");
            return;
        }

        var interval = PositiveTimeSpanSetting(IntervalConfigurationKey, DefaultInterval);
        var staleAfter = PositiveTimeSpanSetting(StaleAfterConfigurationKey, DefaultStaleAfter);

        using var timer = new PeriodicTimer(interval, timeProvider);
        await TryScanAllScopesAsync(scopes, staleAfter, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryScanAllScopesAsync(scopes, staleAfter, stoppingToken);
        }
    }

    private TimeSpan PositiveTimeSpanSetting(string key, TimeSpan fallback)
    {
        var value = configuration.GetValue(key, fallback);
        if (value > TimeSpan.Zero)
        {
            return value;
        }

        logger.LogWarning(
            "Quality release fact backlog setting {SettingKey} value {SettingValue} is not positive; falling back to {Fallback}.",
            key,
            value,
            fallback);
        return fallback;
    }

    private IEnumerable<WorkOrderReleaseFactBacklogScope> GetConfiguredScopes()
    {
        foreach (var scopeSection in configuration.GetSection(ScopesConfigurationKey).GetChildren())
        {
            var organizationId = scopeSection["OrganizationId"];
            var environmentId = scopeSection["EnvironmentId"];
            if (!string.IsNullOrWhiteSpace(organizationId) && !string.IsNullOrWhiteSpace(environmentId))
            {
                yield return new WorkOrderReleaseFactBacklogScope(organizationId.Trim(), environmentId.Trim());
            }
        }

        var singleOrganizationId = configuration[OrganizationIdConfigurationKey];
        var singleEnvironmentId = configuration[EnvironmentIdConfigurationKey];
        if (!string.IsNullOrWhiteSpace(singleOrganizationId) && !string.IsNullOrWhiteSpace(singleEnvironmentId))
        {
            yield return new WorkOrderReleaseFactBacklogScope(singleOrganizationId.Trim(), singleEnvironmentId.Trim());
        }
    }

    private async Task TryScanAllScopesAsync(
        IReadOnlyCollection<WorkOrderReleaseFactBacklogScope> scopes,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var scope in scopes)
        {
            try
            {
                using var serviceScope = scopeFactory.CreateScope();
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var metrics = serviceScope.ServiceProvider.GetRequiredService<WorkOrderReleaseFactBacklogMetrics>();
                var reading = await metrics.RefreshAsync(
                    dbContext,
                    scope.OrganizationId,
                    scope.EnvironmentId,
                    nowUtc,
                    staleAfter,
                    cancellationToken);
                if (reading.Operations > 0)
                {
                    logger.LogWarning(
                        "{BacklogOperationCount} Quality operations for {OrganizationId}/{EnvironmentId} still have no work-order release facts; the oldest production report is {OldestBacklogAge} old. Run the MES work-order release projection backfill to clear them.",
                        reading.Operations,
                        scope.OrganizationId,
                        scope.EnvironmentId,
                        reading.OldestAge);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Quality work-order release fact backlog scan failed for {OrganizationId}/{EnvironmentId}; the scanner will retry on the next tick.",
                    scope.OrganizationId,
                    scope.EnvironmentId);
            }
        }
    }

    private sealed record WorkOrderReleaseFactBacklogScope(string OrganizationId, string EnvironmentId);
}
