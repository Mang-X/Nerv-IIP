using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Remediation;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// <c>created</c> 存量工单补下达（#3119）的**幂等、边界与判据可复算性**，跑在真实 PostgreSQL 上。
///
/// <para><b>为什么这三件事必须由真实 provider 承担。</b>
/// ① <b>判据可复算</b>：验收判据是票面那条判定 SQL 的行数（跑前有、跑后无），
/// 而它是一条**手写 SQL**——列名、schema、以及 <c>operation_tasks.status</c>
/// 按 <c>HasConversion&lt;string&gt;()</c> 存的是**枚举名**而不是序号，这三件事 EF Core InMemory 一件也证不了。
/// 本用例直接执行那条 SQL，让「补救选中的行」与「判定 SQL 数到的行」是同一批。
/// ② <b>边界</b>：<c>created</c> × <c>InProgress</c> 这个组合在真实 FK/CHECK 下**造得出**
/// （<c>operation_tasks</c> 只有一条 FK 指向 <c>work_orders</c>，<c>work_orders</c> 的 CHECK 只管
/// <c>version &gt; 0</c> 与返工来源），这一点本身就是本票缺陷可达性的证据。
/// ③ <b>幂等</b>：重跑要跨一次真实提交，InMemory 上「跑两遍」只是同一个变更跟踪器再来一遍。</para>
/// </summary>
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class CreatedWorkOrderReleaseBackfillPostgresTests
{
    private const string Organization = "org-001";
    private const string Environment = "env-dev";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T00:00:00Z");

    /// <summary>#3119 票面的现网影响面判定 SQL，按 lane 的 schema 限定。</summary>
    private static readonly string ImpactQuery = $"""
        SELECT count(DISTINCT wo.work_order_id) AS work_orders,
               count(*) AS operations
        FROM {MesFacts.Schema}.work_orders wo
        JOIN {MesFacts.Schema}.operation_tasks ot
          ON ot.organization_id = wo.organization_id
         AND ot.environment_id  = wo.environment_id
         AND ot.work_order_id   = wo.work_order_id
        WHERE wo.status = 'created'
          AND ot.status IN ('InProgress', 'Paused')
        """;

    [MesRealPostgresFact(Timeout = 60_000)]
    public async Task Backfill_clears_the_impact_query_for_executing_created_work_orders_and_reruns_clean_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        AddWorkOrder(dbContext, "WO-PG-3119-RUNNING", OperationTaskLifecycleStatus.InProgress);
        AddWorkOrder(dbContext, "WO-PG-3119-PAUSED", OperationTaskLifecycleStatus.Paused);
        AddWorkOrder(dbContext, "WO-PG-3119-QUEUED", OperationTaskLifecycleStatus.Queued);
        AddWorkOrder(dbContext, "WO-PG-3119-CANCELLED", OperationTaskLifecycleStatus.Cancelled);
        AddWorkOrder(dbContext, "WO-PG-3119-RELEASED", OperationTaskLifecycleStatus.InProgress, release: true);
        await dbContext.SaveChangesAsync();

        // 跑之前：判定 SQL 确实数得到这两行。**这条断言是本票验收的阳性对照**——
        // 在 0 行上跑补救得到的绿只证明「没有阴性误伤」，证不了「补救真的选中了什么」。
        Assert.Equal((2, 2), await QueryImpactAsync());

        var first = await Backfill(dbContext);
        await dbContext.SaveChangesAsync();

        Assert.Equal(4, first.CreatedWorkOrdersScanned);
        Assert.Equal(2, first.WorkOrdersReleased);
        Assert.Equal(2, first.OperationsReleased);
        // 报告里的这个数与判定 SQL 跑前数到的 operations 是同一个量，运维可以直接对上。
        Assert.Equal(2, first.ExecutingOperationsRemediated);
        Assert.Equal((0, 0), await QueryImpactAsync());
        Assert.Equal(
            new[] { "WO-PG-3119-CANCELLED", "WO-PG-3119-QUEUED" },
            await StillCreatedWorkOrderIdsAsync(dbContext));

        // 重跑：跨过上面那次真实提交，第二次既不再补下达、也不改变判定 SQL 的读数。
        var second = await Backfill(dbContext);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, second.CreatedWorkOrdersScanned);
        Assert.Equal(0, second.WorkOrdersReleased);
        Assert.Equal(0, second.OperationsReleased);
        Assert.Equal(0, second.ExecutingOperationsRemediated);
        Assert.Equal((0, 0), await QueryImpactAsync());
        Assert.Equal(
            new[] { "WO-PG-3119-CANCELLED", "WO-PG-3119-QUEUED" },
            await StillCreatedWorkOrderIdsAsync(dbContext));
    }

    private static async Task<(long WorkOrders, long Operations)> QueryImpactAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = ImpactQuery;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task<string[]> StillCreatedWorkOrderIdsAsync(ApplicationDbContext dbContext) =>
        await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.Status == WorkOrder.CreatedStatus)
            .Select(x => x.WorkOrderIdValue)
            .OrderBy(x => x)
            .ToArrayAsync();

    private static async Task<CreatedWorkOrderReleaseBackfillReport> Backfill(ApplicationDbContext dbContext) =>
        await new BackfillCreatedWorkOrderReleaseCommandHandler(dbContext).Handle(
            new BackfillCreatedWorkOrderReleaseCommand(),
            CancellationToken.None);

    private static void AddWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        OperationTaskLifecycleStatus operationStatus,
        bool release = false)
    {
        var workOrder = WorkOrder.Create(
            Organization, Environment, workOrderId, "SKU-FG-1000", "PV-FG-1000",
            quantity: 1000m, priority: 1, dueUtc: Now.AddDays(3));
        if (release)
        {
            workOrder.MarkReleased();
        }

        workOrder.ClearDomainEvents();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            Organization, Environment, workOrderId, $"OP-{workOrderId}-10",
            operationStatus, 10, "WC-010", [],
            Now.AddDays(-2), TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m));
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
