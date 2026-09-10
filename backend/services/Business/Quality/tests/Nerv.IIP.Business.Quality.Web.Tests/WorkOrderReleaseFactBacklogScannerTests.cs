using DotNetCore.CAP;
using Nerv.IIP.Messaging.CAP;
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
/// 合同来源：`Regression` —— 开关关闭无副作用、按 scope 遍历与年龄下限的默认取值都是 #2983 收窄后
/// 写在票面上的验收条件，本类按那些条件构造最小夹具。年龄下限那两条另有一层
/// `ProviderBehavior` 的权威来源：CAP 自己的 <c>CapOptions</c> 重投预算契约
/// （见 <see cref="Default_stale_after_floor_covers_the_cap_automatic_redelivery_budget"/> 与
/// <see cref="Configured_cap_retry_interval_raises_the_default_floor_with_it"/>）。
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
    /// <c>FailedRetryCount × FailedRetryInterval</c>。
    ///
    /// 没有任何配置时适用 CAP 自身默认值，默认下限必须**覆盖**该预算，否则 Gauge 会把还能自愈的行
    /// 计成积压。CAP 改默认值时本用例会红，届时须重算基线，而不是改断言。
    /// </summary>
    [Fact]
    public void Default_stale_after_floor_covers_the_cap_automatic_redelivery_budget()
    {
        var capDefaults = new CapOptions();
        var redeliveryBudget = TimeSpan.FromSeconds((double)capDefaults.FailedRetryCount * capDefaults.FailedRetryInterval);
        var resolved = WorkOrderReleaseFactBacklogScanner.ResolveDefaultStaleAfter(
            new ConfigurationBuilder().Build());

        Assert.True(
            redeliveryBudget <= resolved,
            $"CAP redelivery budget {redeliveryBudget} (FailedRetryCount={capDefaults.FailedRetryCount}, "
            + $"FailedRetryInterval={capDefaults.FailedRetryInterval}s) exceeds the resolved backlog floor {resolved}.");
        Assert.Equal(WorkOrderReleaseFactBacklogScanner.BaselineStaleAfter, resolved);
    }

    /// <summary>
    /// 真正生效的自愈预算由 <c>Cap:FailedRetryInterval</c> 决定，而它**是可配置的**。
    /// 若下限只钉在包默认值上，有人把该键调大后下限就静默失效，而断言照绿——这条推导最容易守错的
    /// 就是这个变量。因此默认下限读的是生效值，跟着预算一起抬高；调小时不下探基线。
    /// </summary>
    [Theory]
    [InlineData("600", 500 * 60)]
    [InlineData("120", 100 * 60)]
    [InlineData("1", 60 * 60)]
    [InlineData("", 60 * 60)]
    [InlineData("not-an-int", 60 * 60)]
    public void Configured_cap_retry_interval_raises_the_default_floor_with_it(
        string configuredRetryIntervalSeconds,
        int expectedFloorSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CapMessagingConfiguration.FailedRetryIntervalConfigurationKey] = configuredRetryIntervalSeconds,
            })
            .Build();

        Assert.Equal(
            TimeSpan.FromSeconds(expectedFloorSeconds),
            WorkOrderReleaseFactBacklogScanner.ResolveDefaultStaleAfter(configuration));
    }

    /// <summary>
    /// 上一条是算术；这一条是行为：把 <c>Cap:FailedRetryInterval</c> 调大而**不**显式给
    /// <c>StaleAfter</c>，一条两小时前的报工事实必须仍被当作「还在自愈窗口内」而不计入积压。
    /// </summary>
    [Fact]
    public async Task Raised_cap_retry_interval_keeps_a_still_self_healing_row_out_of_the_backlog()
    {
        var databaseName = $"cap-budget-{Guid.CreateVersion7():N}";
        await using (var seed = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(databaseName))
        {
            // 报工事实比巡检时刻早两小时：在发布默认下限（1 小时）下它会被计入积压。
            await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
                seed, "WO-STUCK", "OP-001", "RPT-STUCK", WorkOrderReleaseFactBacklogFixture.ScanAtUtc.AddHours(-2));
        }

        var probe = new ScopeResolutionProbe();
        var registry = Metrics.NewCustomRegistry();
        await using var services = BuildServices(probe, registry, databaseName);
        var clock = new TimerRegistrationObservingTimeProvider(WorkOrderReleaseFactBacklogFixture.ScanAtUtc);
        // 50 × 600s = 500 分钟 > 2 小时：该行仍在 CAP 的自愈窗口内。
        var scanner = new WorkOrderReleaseFactBacklogScanner(
            services.GetRequiredService<IServiceScopeFactory>(),
            ConfigurationWithoutStaleAfter(capRetryIntervalSeconds: "600"),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            clock);

        await scanner.StartAsync(CancellationToken.None);
        await clock.WaitForFirstTimerAsync();
        await WaitForScopeScansAsync(probe, 1);
        var raisedFloor = await WaitForPublishedSamplesAsync(registry);
        await scanner.StopAsync(CancellationToken.None);

        Assert.Equal(
            0d,
            raisedFloor[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);

        // 对照：同一行、同一时刻，只把 CAP 重投间隔换回默认值，它就必须被计成积压——
        // 否则上面那个 0 证明的只是"这行本来就不算积压"，而不是"下限跟着预算抬高了"。
        var baselineProbe = new ScopeResolutionProbe();
        var baselineRegistry = Metrics.NewCustomRegistry();
        await using var baselineServices = BuildServices(baselineProbe, baselineRegistry, databaseName);
        var baselineClock = new TimerRegistrationObservingTimeProvider(WorkOrderReleaseFactBacklogFixture.ScanAtUtc);
        var baselineScanner = new WorkOrderReleaseFactBacklogScanner(
            baselineServices.GetRequiredService<IServiceScopeFactory>(),
            ConfigurationWithoutStaleAfter(capRetryIntervalSeconds: null),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            baselineClock);

        await baselineScanner.StartAsync(CancellationToken.None);
        await baselineClock.WaitForFirstTimerAsync();
        await WaitForScopeScansAsync(baselineProbe, 1);
        var baselineFloor = await WaitForPublishedSamplesAsync(baselineRegistry);
        await baselineScanner.StopAsync(CancellationToken.None);

        Assert.Equal(
            1d,
            baselineFloor[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
    }

    private static IConfiguration ConfigurationWithoutStaleAfter(string? capRetryIntervalSeconds)
    {
        var values = new Dictionary<string, string?>
        {
            ["Quality:ReleaseFactBacklog:Enabled"] = "true",
            ["Quality:ReleaseFactBacklog:Interval"] = "00:15:00",
            ["Quality:ReleaseFactBacklog:Scopes:0:OrganizationId"] = WorkOrderReleaseFactBacklogFixture.OrganizationId,
            ["Quality:ReleaseFactBacklog:Scopes:0:EnvironmentId"] = WorkOrderReleaseFactBacklogFixture.EnvironmentId,
        };
        if (capRetryIntervalSeconds is not null)
        {
            values[CapMessagingConfiguration.FailedRetryIntervalConfigurationKey] = capRetryIntervalSeconds;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
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

    /// <summary>
    /// 「scope 已被扫过」与「读数已经写进导出面」不是同一件事：巡检先解析出 DbContext，之后才
    /// <c>Set</c> 两个 Gauge。按前者取样会在两者之间取到空导出面，用例随机红。
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, double>> WaitForPublishedSamplesAsync(CollectorRegistry registry) =>
        await Eventually.WaitAsync(
            "the release fact backlog scanner to publish both gauges on the exposition endpoint",
            async _ => await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry),
            samples => samples.ContainsKey(
                WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)),
            samples => $"published samples={samples.Count}",
            new EventuallyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), []));

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
