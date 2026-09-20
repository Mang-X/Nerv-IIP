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
            ScopeScanObservingScopeFactory.Around(services, probe),
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
            ScopeScanObservingScopeFactory.Around(services, probe),
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
            ScopeScanObservingScopeFactory.Around(services, probe),
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
            ScopeScanObservingScopeFactory.Around(services, probe),
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
            ScopeScanObservingScopeFactory.Around(services, probe),
            ConfigurationWithoutStaleAfter(capRetryIntervalSeconds: "600"),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            clock);

        await scanner.StartAsync(CancellationToken.None);
        await clock.WaitForFirstTimerAsync();
        await WaitForScopeScansAsync(probe, 1);
        var raisedFloor = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);
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
            ScopeScanObservingScopeFactory.Around(baselineServices, baselineProbe),
            ConfigurationWithoutStaleAfter(capRetryIntervalSeconds: null),
            NullLogger<WorkOrderReleaseFactBacklogScanner>.Instance,
            baselineClock);

        await baselineScanner.StartAsync(CancellationToken.None);
        await baselineClock.WaitForFirstTimerAsync();
        await WaitForScopeScansAsync(baselineProbe, 1);
        var baselineFloor = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(baselineRegistry);
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
    /// 「scope 已被解析出 DbContext」与「该 scope 的读数已经写进导出面」不是同一件事：巡检先解析
    /// DbContext，跑完查询之后才 <c>Set</c> 两个 Gauge。按前者取样落在这个窗口里，按 label 取值的
    /// 断言就抛 <see cref="KeyNotFoundException"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 在 merge-base 上隔离取样 n=20 的形态分布：<strong>18/20</strong> 是第二个 scope 的**两个族都
    /// 还没发布**（本注释原本描述的形态）；<strong>1/20</strong> 是 <c>oldest_age{…env-prod}</c> 在
    /// 而 <c>operations{…env-prod}</c> 缺——导出按族依次快照，取样本身不是原子操作，所以会读到
    /// **残缺**导出面；余下 1/20 恰好赶上两族都已发布。⚠️ 后一种只占约 5%，⛔ 不要把它当主因去查。
    /// </para>
    /// <para>
    /// 因此这里等的是巡检**自己发布的边沿**，而不是墙钟轮询一个更早的计数器。被顶掉的旧写法有两层
    /// 病：等待信号本身早于发布（<c>probe.DbContextResolutions</c>），以及「已发布」那个谓词只查
    /// **默认 label 组**，对配了第二个 scope 的用例零保护——两层都放行了同一个竞速。
    /// </para>
    /// </remarks>
    private static Task WaitForScopeScansAsync(ScopeResolutionProbe probe, int expected) =>
        probe.ScopeScansCompleted.WaitForAsync(expected);

    /// <summary>
    /// 把「巡检完成了第 N 个 scope 的扫描」包装成可等待的边沿：装饰 <see cref="IServiceScopeFactory"/>，
    /// 在每个 per-scope DI scope 被释放的那一刻发布。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 释放点是从外部能观测到的、**绝不早于**该 scope 两次 <c>Set</c> 的位置：
    /// <c>WorkOrderReleaseFactBacklogScanner.TryScanAllScopesAsync</c> 用 <c>using var serviceScope</c>
    /// 把「解析 DbContext → <c>RefreshAsync</c>（内含两次 <c>Set</c>）→ 记日志」整段括在里面，正常出口
    /// 与异常出口都在其后释放。相对地，<see cref="ScopeResolutionProbe.RecordDbContextResolution"/>
    /// 括住的只是那一段的**开头**。
    /// </para>
    /// <para>
    /// ⚠️ 措辞是「绝不早于」而不是「严格晚于」，因为这两件事在**异常**路径上会脱钩：扫描抛异常时
    /// <c>TryScanAllScopesAsync</c> 的 <c>catch</c> 把它吞掉记日志，scope 照样释放、边沿照样发，但那一轮
    /// **没有 <c>Set</c>**。此时用例仍然红，只是失败形态从「超时 + 边沿诊断」退化成取键的
    /// <see cref="KeyNotFoundException"/>——鉴别力不丢，可读性降级。
    /// </para>
    /// </remarks>
    private sealed class ScopeScanObservingScopeFactory(IServiceScopeFactory inner, ScopeResolutionProbe probe)
        : IServiceScopeFactory
    {
        internal static IServiceScopeFactory Around(IServiceProvider services, ScopeResolutionProbe probe) =>
            new ScopeScanObservingScopeFactory(services.GetRequiredService<IServiceScopeFactory>(), probe);

        public IServiceScope CreateScope() => new ObservedScope(inner.CreateScope(), probe);

        private sealed class ObservedScope(IServiceScope inner, ScopeResolutionProbe probe) : IServiceScope
        {
            public IServiceProvider ServiceProvider => inner.ServiceProvider;

            public void Dispose()
            {
                inner.Dispose();
                probe.RecordScopeScanCompleted();
            }
        }
    }

    private sealed class ScopeResolutionProbe
    {
        private int dbContextResolutions;
        private int metricsResolutions;

        public int DbContextResolutions => Volatile.Read(ref dbContextResolutions);

        public int MetricsResolutions => Volatile.Read(ref metricsResolutions);

        /// <summary>「巡检已完成第 N 个 scope 的扫描」的边沿。</summary>
        public CountingEdgeSignal ScopeScansCompleted { get; } =
            new("the scanner to finish a release fact backlog scope scan");

        public void RecordDbContextResolution() => Interlocked.Increment(ref dbContextResolutions);

        public void RecordMetricsResolution() => Interlocked.Increment(ref metricsResolutions);

        public void RecordScopeScanCompleted() => ScopeScansCompleted.Record();
    }
}
