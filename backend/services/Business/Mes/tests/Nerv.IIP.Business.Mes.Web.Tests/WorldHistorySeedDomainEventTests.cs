using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// L1 背景历史引擎（MES 侧）**不得驱动下游**的门禁。
///
/// <para>两条用例分工明确：
/// <list type="number">
/// <item>第一条是**框架事实的实测**——裸 <c>DbContext.SaveChangesAsync()</c> 派不派发领域事件，
///       并用同一个 <c>DbContext</c> 上的 <c>IUnitOfWork.SaveEntitiesAsync()</c> 做阳性对照，
///       证明「录到 0 条」是真的没派发，而不是探针本身失灵；</item>
/// <item>第二条是**种子自身的门禁**——种子写盘那一刻，被跟踪实体上不许还挂着领域事件。
///       它不依赖第一条的结论：就算哪天框架改成裸 <c>SaveChanges</c> 也派发，这条依然把种子按住。</item>
/// </list></para>
/// </summary>
public sealed class WorldHistorySeedDomainEventTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>种子用例规模：0.02 已覆盖工单 / 工序 / 领料 / 报工 / 完工入库全链，又不让 InMemory provider 变慢。</summary>
    private const double TestScale = 0.02d;

    /// <summary>
    /// 实测本仓栈里领域事件的派发边界：裸 <c>SaveChangesAsync</c> 一条都不发，
    /// <c>IUnitOfWork.SaveEntitiesAsync</c> 才发。
    ///
    /// <para>这是「种子为什么可以先写后清、而不是必须走别的写法」的事实依据，
    /// 也是「种子那份 <c>ClearDomainEvents</c> 属于防御式，而非当下就在灭火」的依据。
    /// 结论必须来自实测：注释和文档都不算数。</para>
    /// </summary>
    [Fact]
    public async Task Plain_save_changes_does_not_dispatch_domain_events_but_unit_of_work_does()
    {
        var recorder = new PublishedNotificationRecorder();
        await using var provider = BuildProvider(recorder, out _);
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 第一路：种子实际走的路径——裸 SaveChangesAsync。
        var viaSaveChanges = CreateReleasedWorkOrder("WO-PROBE-SAVECHANGES");
        dbContext.WorkOrders.Add(viaSaveChanges);
        await dbContext.SaveChangesAsync();

        var afterSaveChanges = recorder.Names.ToArray();
        var stillPending = viaSaveChanges.GetDomainEvents().Select(x => x.GetType().Name).Order().ToArray();
        output.WriteLine($"裸 SaveChangesAsync 派发到 MediatR 的通知：{Describe(afterSaveChanges)}");
        output.WriteLine($"裸 SaveChangesAsync 之后仍挂在聚合上的领域事件：{Describe(stillPending)}");

        Assert.True(
            afterSaveChanges.Length == 0,
            $"裸 DbContext.SaveChangesAsync() 竟然派发了领域事件：{Describe(afterSaveChanges)}——" +
            "种子的前提（历史事实不会外发）已经不成立，必须改写种子的落盘方式而不只是清事件。");
        // 反过来证明「0 条」不是因为压根没有事件可发：事件确实生成了，只是没人取走。
        Assert.Equal(
            ["WorkOrderCreatedDomainEvent", "WorkOrderReleasedDomainEvent"],
            stillPending);

        // 第二路：阳性对照——netcorepal 的 UnitOfWork 路径。
        var viaUnitOfWork = CreateReleasedWorkOrder("WO-PROBE-UNITOFWORK");
        dbContext.WorkOrders.Add(viaUnitOfWork);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveEntitiesAsync();

        var afterUnitOfWork = recorder.Names.ToArray();
        output.WriteLine($"IUnitOfWork.SaveEntitiesAsync 派发到 MediatR 的通知：{Describe(afterUnitOfWork)}");

        Assert.Contains("WorkOrderReleasedDomainEvent", afterUnitOfWork);
        Assert.Contains("WorkOrderCreatedDomainEvent", afterUnitOfWork);
    }

    /// <summary>
    /// 种子写盘那一刻，被跟踪实体上不许还挂着领域事件。
    ///
    /// <para>本块写的每一步都会 <c>AddDomainEvent</c>，且多数有跨服务转换器：
    /// <c>WorkOrderReleased/Completed/Closed</c>、<c>OperationTaskCompleted</c>、
    /// <c>ProductionReportRecorded</c>、<c>MaterialIssueRequestCreated/Requested</c> 与
    /// <c>MaterialLineSideReceiptConfirmed</c>（后三者 → Inventory 的
    /// <c>InventoryMovementRequestedIntegrationEvent</c>，是会真扣库存的出库请求）。
    /// 全量规模约 3600 张工单，一旦外发就是启动瞬间几万条 CAP 消息。</para>
    ///
    /// <para>探针挂在 <c>SavingChanges</c> 上——那正是事件「有机会被取走」的时刻，
    /// 比事后翻变更跟踪器可靠（种子每批末尾会 <c>ChangeTracker.Clear()</c>，事后什么都看不到）。</para>
    /// </summary>
    [Fact]
    public async Task Seed_clears_domain_events_before_writing_history_facts()
    {
        var recorder = new PublishedNotificationRecorder();
        await using var provider = BuildProvider(recorder, out var interceptor);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>()
            .SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        output.WriteLine($"写盘时挂在实体上的领域事件（{interceptor.PendingOnSaving.Count}）：{Describe(interceptor.PendingOnSaving)}");
        output.WriteLine($"实际派发出去的通知（{recorder.Names.Count}）：{Describe(recorder.Names)}");
        output.WriteLine($"本次实测覆盖的写盘次数：{interceptor.SaveCount}");

        // 探针有效性：种子确实写过盘，否则「零事件」是空跑出来的假绿。
        Assert.True(interceptor.SaveCount > 0, "种子一次盘都没写，这条用例证明不了任何事。");
        Assert.True(
            interceptor.PendingOnSaving.Count == 0,
            $"种子写盘时仍有 {interceptor.PendingOnSaving.Count} 条领域事件挂在实体上：" +
            $"{Describe(interceptor.PendingOnSaving)}——历史事实会驱动下游（CAP 风暴 / 库存被真扣）。" +
            "落盘前必须 ClearDomainEvents。");
        Assert.Empty(recorder.Names);
    }

    private static string Describe(IReadOnlyCollection<string> names) =>
        names.Count == 0
            ? "（空）"
            : string.Join("; ", names.GroupBy(x => x, StringComparer.Ordinal)
                .Select(g => $"{g.Key}×{g.Count()}")
                .Order(StringComparer.Ordinal));

    private static WorkOrder CreateReleasedWorkOrder(string workOrderId)
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            workOrderId,
            "SKU-PROBE",
            productionVersionId: null,
            quantity: 1m,
            priority: 10,
            dueUtc: new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));
        workOrder.MarkReleased();
        return workOrder;
    }

    /// <summary>
    /// 按 <c>Program.cs</c> 的真实姿势组装：<c>AddDbContext</c> + <c>AddUnitOfWork</c> + MediatR。
    /// 领域事件不论从哪条通道派发，终点都是 MediatR 的通知处理器，所以用开放泛型处理器一网打尽。
    /// </summary>
    private static ServiceProvider BuildProvider(
        PublishedNotificationRecorder recorder,
        out PendingDomainEventInterceptor interceptor)
    {
        var probe = new PendingDomainEventInterceptor();
        interceptor = probe;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseInMemoryDatabase($"mes-world-history-events-{Guid.CreateVersion7():N}");
            options.AddInterceptors(probe);
            // 阳性对照走 IUnitOfWork.SaveEntitiesAsync，它会开事务；InMemory provider 不支持事务，
            // 默认把这条警告升级成异常。这里只是让对照跑得起来——被测的是「派不派发领域事件」，与事务无关。
            options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssemblyContaining<WorldHistorySeedDomainEventTests>()
            .AddUnitOfWorkBehaviors());
        services.AddTransient(typeof(INotificationHandler<>), typeof(RecordingNotificationHandler<>));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<IWorldHistoryProductionVersionResolver, StubProductionVersionResolver>();
        services.AddScoped<WorldHistorySeedService>();
        return services.BuildServiceProvider();
    }

    /// <summary>在 <c>SavingChanges</c> 时把变更跟踪器里所有实体上挂着的领域事件抄下来。</summary>
    private sealed class PendingDomainEventInterceptor : SaveChangesInterceptor
    {
        public List<string> PendingOnSaving { get; } = [];

        public int SaveCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (eventData.Context is { } context)
            {
                foreach (var entry in context.ChangeTracker.Entries<Entity>())
                {
                    PendingOnSaving.AddRange(entry.Entity.GetDomainEvents().Select(x => x.GetType().Name));
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class PublishedNotificationRecorder
    {
        public List<string> Names { get; } = [];
    }

    private sealed class RecordingNotificationHandler<TNotification>(PublishedNotificationRecorder recorder)
        : INotificationHandler<TNotification>
        where TNotification : INotification
    {
        public Task Handle(TNotification notification, CancellationToken cancellationToken)
        {
            recorder.Names.Add(notification!.GetType().Name);
            return Task.CompletedTask;
        }
    }

    private sealed class StubProductionVersionResolver : IWorldHistoryProductionVersionResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<string> skuCodes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                skuCodes.ToDictionary(sku => sku, sku => $"PV-{sku}", StringComparer.Ordinal));
    }
}
