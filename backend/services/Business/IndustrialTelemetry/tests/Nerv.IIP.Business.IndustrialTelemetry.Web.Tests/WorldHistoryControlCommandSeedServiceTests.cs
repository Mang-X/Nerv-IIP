using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

/// <summary>
/// L1 背景历史 **四期**（设备控制指令台账 <c>OPS-WH-*</c>）的门禁测试。
///
/// 关键约束：任意 asOfDate 都必须成立——演示日期一改，指令规模、终态分布、号段格式
/// 与「历史指令一律终态」的安全条款都不能塌。因此规模 / 分布 / 一致性类断言一律走
/// 5 日期 <c>[Theory]</c>。
/// </summary>
public sealed class WorldHistoryControlCommandSeedServiceTests(ITestOutputHelper output)
{
    /// <summary>五个演示候选日期：周日后首日 / 常规日 / 月初 / 春节段 / 月末。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new() { { 2026, 7, 27 }, { 2026, 7, 24 }, { 2026, 8, 3 }, { 2026, 2, 16 }, { 2026, 7, 31 } };

    /// <summary>库写入类用例的规模：全量 29 周 × 46 台在 InMemory 上过慢，0.35 仍能出上百条指令。</summary>
    private const double SmallScale = 0.35d;

    #region 纯函数 Spec：规模 / 号段 / 终态分布

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Control_command_plans_keep_their_shape_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var plans = WorldHistoryControlCommandSpec.BuildCommandPlans(asOfDate, 1.0);
        var alarms = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, 1.0);

        var byType = plans.GroupBy(x => x.CommandType, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} commands={plans.Count} alarms={alarms.Count} "
            + string.Join(' ', byType.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}")));

        // 规模区间：报警处置约占报警的 20%–60%，加上每周每类一条参数下发。
        // 下界取绝对值 40 而不是更高：春节段（2026-02-16）历史只有 6 周，指令量天然小。
        Assert.InRange(plans.Count, 40, 900);
        Assert.InRange((double)byType.GetValueOrDefault("start-stop") / alarms.Count, 0.20d, 0.60d);

        // 三种指令类型在历史里都有样本（write-tag / start-stop / parameter-set）。
        Assert.True(byType.GetValueOrDefault("write-tag") > 0);
        Assert.True(byType.GetValueOrDefault("start-stop") > 0);
        Assert.True(byType.GetValueOrDefault("parameter-set") > 0);

        // 号段格式 + 唯一性 + 时间戳落在历史区间内且单调。
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate, TimeOnly.MinValue, TimeSpan.Zero);
        var upperBound = new DateTimeOffset(asOfDate, TimeOnly.MaxValue, TimeSpan.Zero);
        Assert.Equal(plans.Count, plans.Select(x => x.OperationTaskId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(plans, plan =>
        {
            Assert.StartsWith(WorldHistoryControlCommandSpec.OperationTaskPrefix, plan.OperationTaskId, StringComparison.Ordinal);
            Assert.InRange(plan.OperationTaskId.Length, 1, 100);
            Assert.InRange(plan.IdempotencyKey.Length, 1, 150);
            Assert.InRange(plan.Reason.Length, 1, 500);
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", plan.Reason);
            Assert.InRange(plan.RequestedAtUtc, lowerBound, upperBound);
            Assert.True(plan.FinishedAtUtc >= plan.RequestedAtUtc);
            Assert.Contains(plan.DeviceAssetId, WorldHistoryDeviceSpec.Devices.Select(x => x.DeviceAssetId));
        });

        // 单点位指令带 tagKey+value 且无参数组；参数组指令反之——与端点校验器同口径。
        Assert.All(plans, plan =>
        {
            if (plan.CommandType == "parameter-set")
            {
                Assert.Null(plan.TagKey);
                Assert.Null(plan.Value);
                Assert.False(string.IsNullOrWhiteSpace(plan.ParametersJson));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(plan.TagKey));
                Assert.False(string.IsNullOrWhiteSpace(plan.Value));
                Assert.Null(plan.ParametersJson);
            }
        });
    }

    /// <summary>① 安全硬条款（纯函数侧）：计划里没有一条会触发下发的待执行态。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void No_planned_command_is_left_in_a_dispatchable_pending_state(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var plans = WorldHistoryControlCommandSpec.BuildCommandPlans(asOfDate, 1.0);

        var outcomes = plans.GroupBy(x => x.TerminalStatus, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} " + string.Join(' ',
            outcomes.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}")));

        Assert.NotEmpty(plans);
        Assert.All(plans, plan =>
        {
            Assert.Contains(plan.TerminalStatus, WorldHistoryControlCommandSpec.TerminalStatuses, StringComparer.Ordinal);
            Assert.DoesNotContain(plan.TerminalStatus, WorldHistoryControlCommandSpec.PendingDispatchStatuses, StringComparer.Ordinal);
            Assert.NotEqual("pending", plan.ExpectedApprovalStatus);
        });

        // 终态配额 17/2/1：成功占多数，失败与驳回都存在（否则演示里看不到失败样本）。
        Assert.InRange((double)outcomes.GetValueOrDefault("completed") / plans.Count, 0.75d, 0.93d);
        Assert.True(outcomes.GetValueOrDefault("failed") > 0);
        Assert.True(outcomes.GetValueOrDefault("rejected") > 0);

        // 驳回只可能出现在需审批的指令上（免审批的指令谈不上「驳回」）。
        Assert.All(plans.Where(x => x.TerminalStatus == "rejected"), plan => Assert.True(plan.RequiresApproval));
        Assert.All(plans.Where(x => x.TerminalStatus == "failed"), plan => Assert.NotNull(plan.FailureCode));
    }

    /// <summary>演示基准日全量 29 周的规模：指令台账落在 300–500 条，写入 PR 实测表。</summary>
    [Fact]
    public void Full_history_at_the_demo_baseline_date_lands_in_the_expected_volume()
    {
        var plans = WorldHistoryControlCommandSpec.BuildCommandPlans(new DateOnly(2026, 7, 28), 1.0);
        var byType = plans.GroupBy(x => x.CommandType, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var byStatus = plans.GroupBy(x => x.TerminalStatus, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

        output.WriteLine($"@scale=1.0 as-of=2026-07-28 commands={plans.Count} "
            + string.Join(' ', byType.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"))
            + " | " + string.Join(' ', byStatus.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}")));

        Assert.InRange(plans.Count, 300, 500);
        Assert.All(plans, plan => Assert.Contains(plan.TerminalStatus, WorldHistoryControlCommandSpec.TerminalStatuses, StringComparer.Ordinal));
    }

    /// <summary>参数下发的设定值恒在正常带内——历史参数指令不该自己制造一起报警。</summary>
    [Fact]
    public void Setpoint_values_never_cross_the_alarm_threshold()
    {
        var plans = WorldHistoryControlCommandSpec.BuildCommandPlans(new DateOnly(2026, 7, 24), 1.0)
            .Where(x => x.CommandType != "start-stop")
            .ToArray();
        var tagsByDeviceClass = WorldHistoryDeviceSpec.DeviceClasses
            .ToDictionary(x => x.CodePrefix, x => x.Tags, StringComparer.Ordinal);

        var checkedValues = 0;
        foreach (var plan in plans)
        {
            var prefix = tagsByDeviceClass.Keys.Single(key => plan.DeviceAssetId.StartsWith(key, StringComparison.Ordinal));
            foreach (var (tagKey, rawValue) in ExtractValues(plan))
            {
                var tag = tagsByDeviceClass[prefix].Single(x => x.TagKey == tagKey);
                var value = decimal.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture);
                if (tag.ComparisonOperator is "<" or "<=")
                {
                    Assert.True(value > tag.AlarmThreshold, $"{plan.OperationTaskId}/{tagKey} {value} <= {tag.AlarmThreshold}");
                }
                else
                {
                    Assert.True(value < tag.AlarmThreshold, $"{plan.OperationTaskId}/{tagKey} {value} >= {tag.AlarmThreshold}");
                }

                checkedValues++;
            }
        }

        output.WriteLine($"setpoint-values-checked={checkedValues}");
        Assert.True(checkedValues > 0);
    }

    [Fact]
    public void Command_plans_are_deterministic_and_independent_of_scale_position()
    {
        var asOfDate = new DateOnly(2026, 7, 24);
        var first = WorldHistoryControlCommandSpec.BuildCommandPlans(asOfDate, 1.0);
        var second = WorldHistoryControlCommandSpec.BuildCommandPlans(asOfDate, 1.0);
        Assert.Equal(first, second);

        // 同一任务号在小规模跑与全量跑里得到同一终态（配额取自任务号，不取全局序号）。
        var small = WorldHistoryControlCommandSpec.BuildCommandPlans(asOfDate, 0.35)
            .ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        var full = first.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        var shared = small.Keys.Where(full.ContainsKey).ToArray();
        Assert.NotEmpty(shared);
        Assert.All(shared, key => Assert.Equal(full[key].TerminalStatus, small[key].TerminalStatus));
    }

    #endregion

    #region 库写入：幂等 + 终态硬断言

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Seed_writes_control_commands_idempotently_and_only_in_terminal_states(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var commands = await db.DeviceControlCommands.AsNoTracking().ToArrayAsync();
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} written={first.DeviceControlCommandsWritten} "
            + $"persisted={commands.Length} validator={first.Validation.DeviceControlCommandsChecked}");

        Assert.True(first.DeviceControlCommandsWritten > 0);
        Assert.Equal(first.DeviceControlCommandsWritten, commands.Length);
        Assert.Equal(commands.Length, first.Validation.DeviceControlCommandsChecked);

        // 幂等：重跑写入量为 0，终态不变。
        Assert.Equal(0, second.DeviceControlCommandsWritten);
        Assert.Equal(commands.Length, await db.DeviceControlCommands.CountAsync());
        Assert.Equal(commands.Length, second.Validation.DeviceControlCommandsChecked);

        // ① 硬断言：历史控制指令无一处于会触发下发的待执行态。
        Assert.All(commands, command =>
        {
            Assert.True(command.IsTerminal, $"{command.OperationTaskId} status='{command.Status}'");
            Assert.DoesNotContain(command.Status, WorldHistoryControlCommandSpec.PendingDispatchStatuses, StringComparer.Ordinal);
            Assert.NotNull(command.FinishedAtUtc);
            Assert.True(command.FinishedAtUtc >= command.RequestedAtUtc);
            Assert.NotEqual("pending", command.ApprovalStatus);
            Assert.StartsWith(WorldHistoryControlCommandSpec.OperationTaskPrefix, command.OperationTaskId, StringComparison.Ordinal);

            // 台账登记时刻已回填到下发时刻，历史指令不会显示成「今天刚下的」。
            Assert.Equal(command.RequestedAtUtc, command.RecordedAtUtc);
        });

        // 控制通道与 L0 采集连接器同一分组（绑定页与指令台账必须能对上）。
        Assert.All(commands, command => Assert.Equal(WorldBibleSpec.ControlConnectorHostId, command.ConnectorHostId));
    }

    /// <summary>校验器是 fail-closed 的：手工把一条历史指令改回待执行态必须让校验当场失败。</summary>
    [Fact]
    public async Task Validator_rejects_a_history_command_left_pending_dispatch()
    {
        var asOfDate = new DateOnly(2026, 7, 24);
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var command = await db.DeviceControlCommands.FirstAsync();
        db.Entry(command).Property(x => x.Status).CurrentValue = "approval-pending";
        db.Entry(command).Property(x => x.ApprovalStatus).CurrentValue = "pending";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", asOfDate, SmallScale));
        output.WriteLine(failure.Message);
        Assert.Contains("pending", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    private static IEnumerable<(string TagKey, string Value)> ExtractValues(WorldHistoryControlCommandPlan plan)
    {
        if (plan.CommandType != "parameter-set")
        {
            yield return (plan.TagKey!, plan.Value!);
            yield break;
        }

        using var document = System.Text.Json.JsonDocument.Parse(plan.ParametersJson!);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            yield return (property.Name, property.Value.GetString()!);
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iiot-world-history-control-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new ControlCommandTestMediator());
    }

    private sealed class ControlCommandTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
