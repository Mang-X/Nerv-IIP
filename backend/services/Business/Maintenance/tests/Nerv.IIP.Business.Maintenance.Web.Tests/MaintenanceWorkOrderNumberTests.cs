using MediatR;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3852：维修工单正式单号由编码规则 <c>maintenance-work-order</c> 分配（MWO-yyyyMMdd-NNNNNN），
/// 四个开单入口（手工 / 报警、v2、计划到期、点检不合格）都要拿到单号；列表、详情、备件与关键字检索都以它为人读单号。
/// </summary>
public sealed class MaintenanceWorkOrderNumberTests
{
    private static readonly Regex RuleShaped = new(@"^MWO-\d{8}-\d{6}$", RegexOptions.Compiled);

    [Fact]
    public async Task Manual_alarm_and_v2_create_entries_allocate_distinct_rule_numbers()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var coding = new MaintenanceCodingService();
        var dayBefore = $"MWO-{DateTimeOffset.UtcNow:yyyyMMdd}";

        await new CreateMaintenanceWorkOrderCommandHandler(db, coding).Handle(
            new CreateMaintenanceWorkOrderCommand("org-001", "env-dev", "DEV-CNC-01", "high", null, "operator-001", null),
            CancellationToken.None);
        await new CreateMaintenanceWorkOrderCommandHandler(db, coding).Handle(
            new CreateMaintenanceWorkOrderCommand("org-001", "env-dev", "DEV-CNC-01", "high", "alarm-001", "operator-001", null),
            CancellationToken.None);
        await new CreateMaintenanceWorkOrderV2CommandHandler(db, coding).Handle(
            new CreateMaintenanceWorkOrderV2Command("org-001", "env-dev", "DEV-CNC-02", "medium", null, "operator-001", null),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var numbers = await db.MaintenanceWorkOrders.AsNoTracking().Select(x => x.WorkOrderNo).ToListAsync();
        Assert.Equal(3, numbers.Count);
        Assert.All(numbers, number => Assert.Matches(RuleShaped, number));
        Assert.Equal(3, numbers.Distinct(StringComparer.Ordinal).Count());
        // 分配日取的是真实时钟：前后各取一次，跨过 UTC 午夜时两天都算对，不随运行时刻变红。
        var dayAfter = $"MWO-{DateTimeOffset.UtcNow:yyyyMMdd}";
        Assert.All(numbers, number => Assert.Contains(number[..12], new[] { dayBefore, dayAfter }));
    }

    [Fact]
    public async Task Plan_generation_and_failed_inspection_also_allocate_rule_numbers()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var coding = new MaintenanceCodingService();
        var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-0001", "P7D", new DateOnly(2026, 9, 1), "maintenance");
        db.MaintenancePlans.Add(plan);
        await db.SaveChangesAsync();

        await new GenerateDueMaintenanceWorkOrdersCommandHandler(db, codingService: coding).Handle(
            new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 9, 1), "system:pm-scheduler"),
            CancellationToken.None);
        await new RecordMaintenanceInspectionCommandHandler(db, coding).Handle(
            new RecordMaintenanceInspectionCommand("org-001", "env-dev", plan.Id, null, "inspector-001", "failed", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var workOrders = await db.MaintenanceWorkOrders.AsNoTracking().ToListAsync();
        Assert.Contains(workOrders, x => x.SourceType == MaintenanceWorkOrderSourceTypes.Plan && RuleShaped.IsMatch(x.WorkOrderNo));
        Assert.Contains(workOrders, x => x.SourceType == MaintenanceWorkOrderSourceTypes.Inspection && RuleShaped.IsMatch(x.WorkOrderNo));
        Assert.Equal(workOrders.Count, workOrders.Select(x => x.WorkOrderNo).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 审核阻断 2：走生产路径（DI + EF 存储，不用内存分配器）。第一次报警建单时外层不提交（模拟 UoW 回滚），
    /// 同一个报警在新 scope 里重试，必须拿回第一次分到的 000001——「键 → 号」绑定已在独立 scope 里提交。
    /// 绑定改回挂在外层 DbContext，或报警分支不传意图键，重试都会分到 000002。
    /// </summary>
    [Fact]
    public async Task Alarm_retry_after_an_uncommitted_first_attempt_gets_the_same_number_back()
    {
        await using var provider = CreateSharedDatabaseProvider();
        async Task<string> AttemptAsync(bool commit)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coding = scope.ServiceProvider.GetRequiredService<MaintenanceCodingService>();
            var result = await new CreateMaintenanceWorkOrderCommandHandler(db, coding).Handle(
                new CreateMaintenanceWorkOrderCommand("org-001", "env-dev", "DEV-CNC-01", "high", "alarm-retry-001", "system:alarm", null),
                CancellationToken.None);
            if (commit)
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }

            return db.MaintenanceWorkOrders.Local.Single(x => x.Id == result.WorkOrderId).WorkOrderNo;
        }

        var first = await AttemptAsync(commit: false);
        var retried = await AttemptAsync(commit: true);

        Assert.Matches(RuleShaped, first);
        Assert.EndsWith("-000001", first, StringComparison.Ordinal);
        Assert.Equal(first, retried);
        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(first, Assert.Single(await verifyDb.MaintenanceWorkOrders.AsNoTracking().ToListAsync()).WorkOrderNo);
    }

    /// <summary>
    /// 审核阻断 1：报警 ID、幂等键入参各自最长 150，拼成意图键后仍须落得进绑定表 150 列宽；
    /// 长度在这里直接断言，不依赖内存库（内存库不校验列宽）。
    /// </summary>
    [Fact]
    public void Intent_keys_have_a_fixed_length_within_the_binding_column_at_the_150_character_boundary()
    {
        var at150 = new string('x', 150);
        var keys = new[]
        {
            MaintenanceWorkOrderNumbers.CreateIntent(at150, null)!,
            MaintenanceWorkOrderNumbers.CreateIntent(null, at150)!,
            MaintenanceWorkOrderNumbers.CreateIntent("a", null)!,
            MaintenanceWorkOrderNumbers.CreateIntent(null, "k")!,
            MaintenanceWorkOrderNumbers.PlanIntent(new string('p', 100), "runtime:1234567.123456:99"),
            MaintenanceWorkOrderNumbers.InspectionIntent(Guid.NewGuid().ToString()),
        };

        Assert.All(keys, key => Assert.True(key.Length <= CodeIdempotencyKey.IdempotencyKeyMaxLength, key));
        Assert.Equal(6 + 64, keys[0].Length);
        Assert.Equal(keys[0].Length, keys[2].Length);
        Assert.Equal(7 + 64, keys[1].Length);
        Assert.Equal(keys[1].Length, keys[3].Length);
        Assert.StartsWith("alarm:", keys[0], StringComparison.Ordinal);
        Assert.StartsWith("create:", keys[1], StringComparison.Ordinal);
        Assert.Null(MaintenanceWorkOrderNumbers.CreateIntent(null, null));
        Assert.Equal(MaintenanceWorkOrderNumbers.CreateIntent(" alarm-1 ", null), MaintenanceWorkOrderNumbers.CreateIntent("alarm-1", null));
        Assert.NotEqual(MaintenanceWorkOrderNumbers.CreateIntent("alarm-1", null), MaintenanceWorkOrderNumbers.CreateIntent("alarm-2", null));
    }

    [Fact]
    public async Task List_detail_spare_parts_and_keyword_search_carry_the_formal_number()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var target = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "MWO-20260928-000007", "DEV-CNC-01", "high", "operator-001");
        var other = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "MWO-20260928-000008", "DEV-CNC-01", "high", "operator-001");
        target.AddSparePartLine(new SparePartLineDraft("SPARE-001", 1m, "pcs", "SITE-001", "loc-spare-01"));
        db.MaintenanceWorkOrders.AddRange(target, other);
        await db.SaveChangesAsync();

        var searched = await new ListMaintenanceWorkOrdersQueryHandler(db).Handle(
            new ListMaintenanceWorkOrdersQuery("org-001", "env-dev", Keyword: "000007"),
            CancellationToken.None);
        var detail = await new GetMaintenanceWorkOrderQueryHandler(db).Handle(
            new GetMaintenanceWorkOrderQuery("org-001", "env-dev", target.Id),
            CancellationToken.None);
        var spareParts = await new ListMaintenanceSparePartsQueryHandler(db).Handle(
            new ListMaintenanceSparePartsQuery("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal("MWO-20260928-000007", Assert.Single(searched.Items).WorkOrderNo);
        Assert.Equal("MWO-20260928-000007", detail.WorkOrder.WorkOrderNo);
        Assert.Equal("MWO-20260928-000007", Assert.Single(spareParts.Items).WorkOrderNo);
    }

    private static ServiceProvider CreateSharedDatabaseProvider()
    {
        var databaseName = $"maintenance-number-shared-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddScoped<IMediator, NoopMediator>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<MaintenanceCodingService>();
        return services.BuildServiceProvider();
    }

    private sealed class NoopMediator : IMediator
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
