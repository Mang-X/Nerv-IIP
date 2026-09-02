using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 回填的**生产组合**：内部端点 → MediatR → <c>AddUnitOfWorkBehaviors()</c> 的事务 behavior → handler → CAP 发布。
/// 其余全部单测都是直接 <c>new BackfillWorkOrderReleaseProjectionCommandHandler(...)</c>，pipeline 整条不在场；
/// 唯一挂上 pipeline 的 HTTP 用例又把入口 <c>ISender</c> 换成了桩。因此「一次回填 = 一个事务包住整轮扫描与
/// 全部 CAP 发布」这条主张在本用例之前**没有任何一层执行过**。
///
/// 同时这也是判据谓词第一次在真实 provider 上**执行**（而不是只生成 SQL 文本）：
/// <c>NOT IN</c> 与 keyset 续扫由 PostgreSQL 自己求值。
/// </summary>
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderReleaseProjectionBackfillPostgresTests
{
    /// <summary>页大小 200，取 201 条批量工单让扫描必然跨页（201 + RELEASED + COMPLETED = 203）。</summary>
    private const int BulkWorkOrderCount = 201;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-02T00:00:00Z");

    [PostgreSqlFact]
    public async Task Backfill_runs_inside_one_uow_transaction_and_publishes_every_gate_bound_work_order_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using (var seed = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(seed);
            await seed.Database.MigrateAsync();
            AddWorkOrder(seed, "WO-PG-RELEASED", workOrder => workOrder.MarkReleased());
            // 已达计划量翻 completed、工序仍在跑：这是 #3000 判据的会失败状态，
            // 它必须由真实 SQL 的 NOT IN 谓词选中，而不是只在 InMemory 上被选中。
            AddWorkOrder(seed, "WO-PG-COMPLETED", workOrder =>
            {
                workOrder.MarkReleased();
                workOrder.Start(Now);
                workOrder.RecordProductionProgress(1000m, 0m, Now);
            });
            AddWorkOrder(seed, "WO-PG-CANCELLED", workOrder => workOrder.Cancel("不再生产", Now));
            AddWorkOrder(seed, "WO-PG-CREATED", static _ => { });
            // 续扫必须真的跨页：页大小 200，故门禁内工单数取 203。
            // **倒序插入**是这条用例的要害——若按升序播种，物理顺序恰等于 keyset 顺序，
            // 删掉 OrderBy 也会全绿（等价于「夹具插入序恰等于排序序」这个假绿陷阱）。
            for (var index = BulkWorkOrderCount - 1; index >= 0; index--)
            {
                AddWorkOrder(
                    seed,
                    $"WO-PG-BULK-{index:D3}",
                    workOrder => workOrder.MarkReleased());
            }
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        // 与 Program.cs:202 同源；handler 从 DI 取时钟。
        services.AddSingleton(TimeProvider.System);
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddMesPostgreSqlPersistence(MesPostgresLaneDatabase.ConnectionString);
        services.AddSingleton<TransactionAwareRecordingPublisher>();
        services.AddSingleton<IMesIntegrationEventOutboxPublisher>(
            sp => sp.GetRequiredService<TransactionAwareRecordingPublisher>());
        await using var provider = services.BuildServiceProvider();

        WorkOrderReleaseProjectionBackfillReport report;
        using (var scope = provider.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<TransactionAwareRecordingPublisher>();
            publisher.Bind(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            report = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new BackfillWorkOrderReleaseProjectionCommand(),
                CancellationToken.None);
        }

        var recorded = provider.GetRequiredService<TransactionAwareRecordingPublisher>();
        var expectedWorkOrderIds = Enumerable
            .Range(0, BulkWorkOrderCount)
            .Select(index => $"WO-PG-BULK-{index:D3}")
            .Concat(["WO-PG-COMPLETED", "WO-PG-RELEASED"])
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedWorkOrderIds.Length, report.WorkOrdersScanned);
        Assert.Equal(expectedWorkOrderIds.Length, report.WorkOrdersPublished);
        // 跨页续扫既不许漏也不许重：错误的 seek 谓词会少发或重发。
        Assert.Equal(expectedWorkOrderIds, recorded.WorkOrderIds.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(expectedWorkOrderIds.Length, recorded.WorkOrderIds.Distinct(StringComparer.Ordinal).Count());
        // 发布顺序即 keyset 的全序；删掉 OrderBy 后倒序播种的物理顺序会在这里露出来。
        Assert.Equal(expectedWorkOrderIds, recorded.WorkOrderIds.ToArray());
        Assert.All(recorded.Topics, topic =>
            Assert.Equal(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent), topic));
        // 「一个事务包住整轮扫描与全部 CAP 发布」——每一次发布都必须发生在已开启的 UoW 事务里，
        // 且全程是同一个事务。
        Assert.All(recorded.TransactionIds, transactionId => Assert.NotNull(transactionId));
        Assert.Single(recorded.TransactionIds.Distinct());
    }

    private static void AddWorkOrder(ApplicationDbContext dbContext, string workOrderId, Action<WorkOrder> advance)
    {
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", workOrderId, "SKU-FG-1000", null,
            quantity: 1000m, priority: 1, dueUtc: Now.AddDays(3));
        advance(workOrder);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001", "env-dev", workOrderId, $"OP-{workOrderId}-10",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-010", [],
            Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m));
    }

    /// <summary>记录每次投递时所处的事务身份；事务由 UoW behavior 提供，不由 handler 自己开。</summary>
    private sealed class TransactionAwareRecordingPublisher : IMesIntegrationEventOutboxPublisher
    {
        private ApplicationDbContext? dbContext;

        public List<string> WorkOrderIds { get; } = [];

        public List<string> Topics { get; } = [];

        public List<Guid?> TransactionIds { get; } = [];

        public void Bind(ApplicationDbContext context) => dbContext = context;

        public Task PublishAsync<T>(string topic, T integrationEvent)
        {
            Topics.Add(topic);
            WorkOrderIds.Add(
                ((WorkOrderReleaseProjectionBackfilledIntegrationEvent)(object)integrationEvent!).Payload.WorkOrderId);
            TransactionIds.Add(dbContext?.Database.CurrentTransaction?.TransactionId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
