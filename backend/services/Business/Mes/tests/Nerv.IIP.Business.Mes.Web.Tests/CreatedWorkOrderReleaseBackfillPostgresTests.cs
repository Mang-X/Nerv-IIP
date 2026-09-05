using System.Net;
using System.Text.Json;
using DotNetCore.CAP;
using DotNetCore.CAP.Persistence;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Remediation;
using Nerv.IIP.Contracts.Mes;

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
        // **补救动作面 ⊋ 判据面**：`Completed` 在 HasExecutionFacts 内（会被补下达），
        // 却**不在**判定 SQL 的 `IN ('InProgress','Paused')` 内（不计入影响面读数）。
        // 这一格只有真实 provider 能证——两侧比的都是 `HasConversion<string>()` 存下来的**枚举名**，
        // InMemory 不做这层往返。它可达（报工带完工即到达），是覆盖缺口而非分支不可达。
        AddWorkOrder(dbContext, "WO-PG-3119-DONE", OperationTaskLifecycleStatus.Completed);
        AddWorkOrder(dbContext, "WO-PG-3119-QUEUED", OperationTaskLifecycleStatus.Queued);
        AddWorkOrder(dbContext, "WO-PG-3119-CANCELLED", OperationTaskLifecycleStatus.Cancelled);
        AddWorkOrder(dbContext, "WO-PG-3119-RELEASED", OperationTaskLifecycleStatus.InProgress, release: true);
        await dbContext.SaveChangesAsync();

        // 跑之前：判定 SQL 确实数得到这两行。**这条断言是本票验收的阳性对照**——
        // 在 0 行上跑补救得到的绿只证明「没有阴性误伤」，证不了「补救真的选中了什么」。
        // 注意它是 2 而不是 3：`WO-PG-3119-DONE` 被补救选中，但判定 SQL 数不到它。
        Assert.Equal((2, 2), await QueryImpactAsync());

        var first = await Backfill(dbContext);
        await dbContext.SaveChangesAsync();

        Assert.Equal(5, first.CreatedWorkOrdersScanned);
        Assert.Equal(3, first.WorkOrdersReleased);
        Assert.Equal(3, first.OperationsReleased);
        // 这个数与判定 SQL 跑前数到的 operations 是同一个量（2），运维可以直接对上；
        // 它与 WorkOrdersReleased（3）**有意不等**——差的正是那张 Completed 工单。
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

    /// <summary>
    /// **发布事实真的离开了 MES**（#3144 复审阻断 7）。
    ///
    /// <para>其余全部用例都直接 <c>new BackfillCreatedWorkOrderReleaseCommandHandler(...)</c>、
    /// DbContext 挂 <c>NoopMediator</c>、由测试自己调 <c>SaveChangesAsync</c>——
    /// 按本仓既定事实「netcorepal 普通 SaveChanges 不派发领域事件」，
    /// **那条路径上派发根本没有被执行过**；载荷形状对，不代表事件出得去。
    /// 而整票的「顺序是硬约束」恰恰建立在「补下达的 <c>WorkOrderReleased</c> 真的到达 Quality」之上
    /// （前置票 #3117 存在的理由就是「否则补下达会进 Quality 死信」）。</para>
    ///
    /// <para>本用例走**生产组合**：真实 <see cref="Program"/> 宿主 → 内部运维端点 →
    /// MediatR pipeline 的 UoW behavior → handler → 领域事件派发 → 集成事件转换 → CAP outbox。
    /// 断言落在 <c>cap.published</c> 的真实行上，不在内存桩上。
    /// 把 handler 挂到一个不派发的 UoW 上（生产里真正会出错的那一支），这条会红。</para>
    /// </summary>
    [MesRealPostgresFact(Timeout = 120_000)]
    public async Task Backfill_publishes_the_release_fact_through_the_real_unit_of_work_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
            await dbContext.Database.MigrateAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStorageInitializer>()
                .InitializeAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IBootstrapper>()
                .BootstrapAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AddWorkOrder(dbContext, "WO-PG-3119-UOW", OperationTaskLifecycleStatus.InProgress);
            AddWorkOrder(dbContext, "WO-PG-3119-UOW-QUEUED", OperationTaskLifecycleStatus.Queued);
            await dbContext.SaveChangesAsync();
        }

        // 前提自检：补下达之前 outbox 里没有任何发布事实，否则下面那条断言可能是种子留下的。
        Assert.Empty(await PublishedReleaseContentsAsync());

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", InternalToken);
        using var response = await client.PostAsync(
            "/internal/business-mes/v1/created-work-order-release-backfill",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("workOrdersReleased").GetInt32());

        var published = await PublishedReleaseContentsAsync();
        var content = Assert.Single(published);
        Assert.Contains("WO-PG-3119-UOW", content, StringComparison.Ordinal);
        // 只被选中的那一张出现在 outbox 里：未选中的 created 工单不许被顺带发布。
        Assert.DoesNotContain("WO-PG-3119-UOW-QUEUED", content, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "WO-PG-3119-UOW-QUEUED" },
            await StillCreatedWorkOrderIdsAsync(factory));
    }

    private const string InternalToken = "test-internal-token-3119";

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "InMemory",
            ["Cap:Version"] = "test-mes-3119-uow",
            ["InternalService:BearerToken"] = InternalToken,
        };
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
            });
    }

    /// <summary>CAP outbox 里所有工单发布事实的载荷。topic 取契约类型短名，与消费侧订阅同源。</summary>
    private static async Task<string[]> PublishedReleaseContentsAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Content\" FROM cap.published WHERE \"Name\" = @name";
        command.Parameters.AddWithValue("name", nameof(WorkOrderReleasedIntegrationEvent));
        var contents = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            contents.Add(reader.GetString(0));
        }

        return [.. contents];
    }

    private static async Task<string[]> StillCreatedWorkOrderIdsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await StillCreatedWorkOrderIdsAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
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
