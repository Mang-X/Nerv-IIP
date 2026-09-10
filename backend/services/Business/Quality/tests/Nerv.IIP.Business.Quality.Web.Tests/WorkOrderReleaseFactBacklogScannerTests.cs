using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Nerv.IIP.Testing;
using Prometheus;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 「工单发布事实缺失」巡检 Service 的外壳用例（#2983）。
///
/// 合同来源：`Regression` + `Governance` —— 开关关闭无副作用、按 scope 遍历与年龄下限的默认取值
/// 都是 #2983 收窄后的验收条件；年龄下限那一条另按 CAP 的自愈预算机检（见
/// <see cref="Default_stale_after_floor_covers_the_cap_automatic_redelivery_budget"/>）。
///
/// provider 边界：EF Core InMemory + <see cref="TimerRegistrationObservingTimeProvider"/> —— 只证明
/// 编排与配置分支，不证明 SQL 翻译，也不证明真实进程里的 <c>/metrics</c> 路由。
/// </summary>
public sealed class WorkOrderReleaseFactBacklogScannerTests
{
    [Fact]
    public async Task Disabled_scanner_creates_no_timer_no_scope_and_publishes_no_sample()
    {
        var probe = new ScopeResolutionProbe();
        var registry = Metrics.NewCustomRegistry();
        await using var services = BuildServices(probe, registry, $"disabled-{Guid.CreateVersion7():N}");
        var clock = new TimerRegistrationObservingTimeProvider(WorkOrderReleaseFactBacklogFixture.ScanAtUtc);
        var scanner = new WorkOrderReleaseFactBacklogScanner(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: false),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            clock);

        await scanner.StartAsync(CancellationToken.None);
        await scanner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await scanner.StopAsync(CancellationToken.None);

        Assert.Equal(0, probe.DbContextResolutions);
        Assert.Equal(0, probe.MetricsResolutions);
        Assert.Equal(0, clock.TimersCreated);
        // 指标对象从未被构造，因此导出面上连这两个族都不存在——这是"无副作用"最强的读数。
        Assert.Empty(await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry));
    }

    [Fact]
    public async Task Enabled_scanner_publishes_one_reading_per_configured_scope_on_every_tick()
    {
        var databaseName = $"enabled-{Guid.CreateVersion7():N}";
        await using (var seed = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(databaseName))
        {
            await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
                seed, "WO-STUCK", "OP-001", "RPT-STUCK", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc);
        }

        var probe = new ScopeResolutionProbe();
        var registry = Metrics.NewCustomRegistry();
        await using var services = BuildServices(probe, registry, databaseName);
        var clock = new TimerRegistrationObservingTimeProvider(WorkOrderReleaseFactBacklogFixture.ScanAtUtc);
        var scanner = new WorkOrderReleaseFactBacklogScanner(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: true),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            clock);

        await scanner.StartAsync(CancellationToken.None);
        await clock.WaitForFirstTimerAsync();
        await WaitForScopeScansAsync(probe, 2);
        var firstTick = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);

        // 第一轮之后把发布事实补上，再推一格：同一条 label 必须被改写成 0。
        await using (var repair = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(databaseName))
        {
            await WorkOrderReleaseFactBacklogFixture.ApplyWorkOrderReleaseAsync(repair, "WO-STUCK", "OP-001");
        }

        clock.Advance(TimeSpan.FromMinutes(15));
        await WaitForScopeScansAsync(probe, 4);
        var secondTick = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);
        await scanner.StopAsync(CancellationToken.None);

        Assert.Equal(1d, firstTick[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
        Assert.Equal(5400d, firstTick[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.OldestBacklogAgeMetric)]);
        // 第二个 scope 没有卡住的行，本轮也必须出读数，否则运维分不清"没卡住"和"没扫过"。
        Assert.Equal(
            0d,
            firstTick[WorkOrderReleaseFactBacklogFixture.Sample(
                WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric,
                environmentId: "env-prod")]);
        Assert.Equal(0d, secondTick[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
        Assert.Equal(0d, secondTick[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.OldestBacklogAgeMetric)]);
    }

    [Fact]
    public async Task Enabled_scanner_without_a_configured_scope_creates_no_timer_and_publishes_no_sample()
    {
        var probe = new ScopeResolutionProbe();
        var registry = Metrics.NewCustomRegistry();
        await using var services = BuildServices(probe, registry, $"no-scope-{Guid.CreateVersion7():N}");
        var clock = new TimerRegistrationObservingTimeProvider(WorkOrderReleaseFactBacklogFixture.ScanAtUtc);
        var scanner = new WorkOrderReleaseFactBacklogScanner(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quality:ReleaseFactBacklog:Enabled"] = "true",
                })
                .Build(),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            clock);

        await scanner.StartAsync(CancellationToken.None);
        await scanner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await scanner.StopAsync(CancellationToken.None);

        Assert.Equal(0, probe.DbContextResolutions);
        Assert.Equal(0, clock.TimersCreated);
        Assert.Empty(await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry));
    }

    [Fact]
    public async Task Non_positive_stale_after_falls_back_to_the_default_floor_instead_of_counting_everything()
    {
        var databaseName = $"bad-floor-{Guid.CreateVersion7():N}";
        await using (var seed = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(databaseName))
        {
            // 只有一条仍在正常传播窗口内的报工：年龄下限若被"0"顶掉，它会被计成卡住的行。
            await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
                seed, "WO-FRESH", "OP-001", "RPT-FRESH", WorkOrderReleaseFactBacklogFixture.FreshReportedAtUtc);
        }

        var probe = new ScopeResolutionProbe();
        var registry = Metrics.NewCustomRegistry();
        await using var services = BuildServices(probe, registry, databaseName);
        var clock = new TimerRegistrationObservingTimeProvider(WorkOrderReleaseFactBacklogFixture.ScanAtUtc);
        var scanner = new WorkOrderReleaseFactBacklogScanner(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: true, staleAfter: "00:00:00"),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            clock);

        await scanner.StartAsync(CancellationToken.None);
        await clock.WaitForFirstTimerAsync();
        await WaitForScopeScansAsync(probe, 2);
        var samples = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);
        await scanner.StopAsync(CancellationToken.None);

        Assert.Equal(0d, samples[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
    }

    /// <summary>
    /// 年龄下限的取值依据机检：投影缺失只有一条不需要人介入的恢复通道——CAP 对
    /// <c>mes.WorkOrderReleased</c> 的自动重投。业务事实非法那一支由 <c>IntegrationEventConsumerGuard</c>
    /// 写死信后正常返回、不抛异常，因此进入 CAP 重试循环的只剩基础设施故障，其预算恰为
    /// <c>FailedRetryCount × FailedRetryInterval</c>。本仓两者都不覆盖，所以适用 CAP 自身的默认值。
    ///
    /// 默认下限必须**覆盖**该预算，否则 Gauge 会把还能自愈的行计成积压。CAP 改默认值时本用例会红，
    /// 届时须重算下限，而不是改断言。
    /// </summary>
    [Fact]
    public void Default_stale_after_floor_covers_the_cap_automatic_redelivery_budget()
    {
        var capDefaults = new CapOptions();
        var redeliveryBudget = TimeSpan.FromSeconds((double)capDefaults.FailedRetryCount * capDefaults.FailedRetryInterval);

        Assert.True(
            redeliveryBudget <= WorkOrderReleaseFactBacklogScanner.DefaultStaleAfter,
            $"CAP redelivery budget {redeliveryBudget} (FailedRetryCount={capDefaults.FailedRetryCount}, "
            + $"FailedRetryInterval={capDefaults.FailedRetryInterval}s) exceeds the default backlog floor "
            + $"{WorkOrderReleaseFactBacklogScanner.DefaultStaleAfter}.");
    }

    private static IConfiguration Configuration(bool enabled, string staleAfter = "01:00:00") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Quality:ReleaseFactBacklog:Enabled"] = enabled ? "true" : "false",
                ["Quality:ReleaseFactBacklog:Interval"] = "00:15:00",
                ["Quality:ReleaseFactBacklog:StaleAfter"] = staleAfter,
                ["Quality:ReleaseFactBacklog:Scopes:0:OrganizationId"] = WorkOrderReleaseFactBacklogFixture.OrganizationId,
                ["Quality:ReleaseFactBacklog:Scopes:0:EnvironmentId"] = WorkOrderReleaseFactBacklogFixture.EnvironmentId,
                ["Quality:ReleaseFactBacklog:Scopes:1:OrganizationId"] = WorkOrderReleaseFactBacklogFixture.OrganizationId,
                ["Quality:ReleaseFactBacklog:Scopes:1:EnvironmentId"] = "env-prod",
            })
            .Build();

    private static ServiceProvider BuildServices(
        ScopeResolutionProbe probe,
        CollectorRegistry registry,
        string databaseName) =>
        new ServiceCollection()
            .AddSingleton(probe)
            .AddSingleton(registry)
            .AddSingleton(serviceProvider =>
            {
                serviceProvider.GetRequiredService<ScopeResolutionProbe>().RecordMetricsResolution();
                return new WorkOrderReleaseFactBacklogMetrics(registry);
            })
            .AddScoped(serviceProvider =>
            {
                serviceProvider.GetRequiredService<ScopeResolutionProbe>().RecordDbContextResolution();
                return WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(databaseName);
            })
            .BuildServiceProvider();

    private static async Task WaitForScopeScansAsync(ScopeResolutionProbe probe, int expected) =>
        await Eventually.WaitAsync(
            "the release fact backlog scanner to finish the expected number of scope scans",
            _ => ValueTask.FromResult(probe.DbContextResolutions),
            observed => observed >= expected,
            observed => $"scope scans={observed}; expected>={expected}",
            new EventuallyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), []));

    private sealed class ScopeResolutionProbe
    {
        private int dbContextResolutions;
        private int metricsResolutions;

        public int DbContextResolutions => Volatile.Read(ref dbContextResolutions);

        public int MetricsResolutions => Volatile.Read(ref metricsResolutions);

        public void RecordDbContextResolution() => Interlocked.Increment(ref dbContextResolutions);

        public void RecordMetricsResolution() => Interlocked.Increment(ref metricsResolutions);
    }
}
