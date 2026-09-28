using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class RushWorkOrderCommandTests
{
    // Contract: Regression. Authority: Issue #3858 — 急单缺生产版本时在建单这一步给业务提示，
    // 不再落一张下达不了的工单（以前要到下达时才撞上带英文码的放行拒绝）。
    [Fact]
    public async Task Rush_work_order_without_production_version_is_rejected_at_creation()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-09-28T08:00:00Z");

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateRushWorkOrderCommandHandler(store).Handle(
                new CreateRushWorkOrderCommand(
                    "org-001", "env-dev", null, "SKU-R", null, 1m,
                    now.AddHours(4), "WC-A", null, 10, TimeSpan.FromHours(1), now,
                    "rush-no-version"),
                CancellationToken.None));

        Assert.Equal("急单必须选择生产版本：请先为该物料选择当前有效的生产版本。", exception.Message);
        Assert.Empty(store.WorkOrders);
    }

    // Contract: Regression. Authority: Issue #3858 — 急单建单时按生产版本冻结齐套需求；
    // 否则齐套读面报「齐套快照缺失」、前端预检永远拦住下达。下达门禁这里不给快照来源，
    // 只能读建单时冻结的那份。
    [Fact]
    public async Task Rush_work_order_freezes_material_requirements_at_creation_so_it_can_be_released()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-09-28T08:00:00Z");
        var snapshots = new StubMaterialSnapshotProvider(MesMaterialRequirementSnapshotResult.Captured(
            "product-engineering-http:PV-R:MBOM-R:A",
            [new MesMaterialRequirementSnapshotLine(
                null, "MAT-R", null, 5m, "PCS", 10m, 0m, "MBOM-R:A:MAT-R", [])]));

        var response = await new CreateRushWorkOrderCommandHandler(
            new PersistentMesPlanningStore(dbContext), new MesCodingService(), dbContext, snapshots)
            .Handle(
                new CreateRushWorkOrderCommand(
                    "org-001", "env-dev", null, "SKU-R", "PV-R", 5m,
                    now.AddHours(4), "WC-A", null, 10, TimeSpan.FromHours(1), now,
                    "rush-freeze-snapshot"),
                CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Equal("PV-R", snapshots.LastRequest!.ProductionVersionId);
        var readiness = await new GetMaterialReadinessQueryHandler(
            dbContext, FrozenMaterialReadinessLiveCoverageProvider.Instance).Handle(
                new GetMaterialReadinessQuery("org-001", "env-dev", response.WorkOrderId),
                CancellationToken.None);
        Assert.Equal("Ready", readiness.ReadinessStatus);
        Assert.Equal(now, readiness.SnapshotCapturedAtUtc);

        var released = await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand("org-001", "env-dev", response.WorkOrderId, now.AddMinutes(5)),
            CancellationToken.None);
        Assert.Equal(response.WorkOrderId, released.ReferenceId);
    }

    [Fact]
    public async Task Rush_work_order_is_not_created_when_material_requirements_cannot_be_frozen()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-09-28T08:00:00Z");

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateRushWorkOrderCommandHandler(
                new PersistentMesPlanningStore(dbContext),
                new MesCodingService(),
                dbContext,
                new StubMaterialSnapshotProvider(
                    MesMaterialRequirementSnapshotResult.Missing("product-engineering:production-version:PV-OLD")))
                .Handle(
                    new CreateRushWorkOrderCommand(
                        "org-001", "env-dev", null, "SKU-R", "PV-OLD", 5m,
                        now.AddHours(4), "WC-A", null, 10, TimeSpan.FromHours(1), now,
                        "rush-snapshot-missing"),
                    CancellationToken.None));

        Assert.Equal("无法按所选生产版本生成齐套需求，急单未创建。请确认该版本当前有效且制造物料清单已发布。", exception.Message);
    }

    private sealed class StubMaterialSnapshotProvider(MesMaterialRequirementSnapshotResult result)
        : IMesMaterialRequirementSnapshotProvider
    {
        public MesMaterialRequirementSnapshotRequest? LastRequest { get; private set; }

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Rush_idempotent_replay_uses_scope_and_work_order_predicate_query()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        await dbContext.Database.EnsureCreatedAsync();
        var coding = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-07-18T08:00:00Z");
        var command = new CreateRushWorkOrderCommand(
            "org-001", "env-dev", "WO-PREDICATE", "SKU-1", "PV-1", 1m,
            now.AddDays(1), "WC-1", "OP-10", 10, TimeSpan.FromMinutes(30), now,
            "rush-predicate-replay");

        await new CreateRushWorkOrderCommandHandler(new InMemoryMesPlanningStore(), coding)
            .Handle(command, CancellationToken.None);
        var persistedWorkOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-PREDICATE", "SKU-1", "PV-1", 1m, 1000, now.AddDays(1));
        persistedWorkOrder.ClearDomainEvents();
        dbContext.WorkOrders.Add(persistedWorkOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        interceptor.Clear();

        var store = new PersistentMesPlanningStore(dbContext);
        var handler = new CreateRushWorkOrderCommandHandler(store, coding, dbContext);
        await handler.Handle(command, CancellationToken.None);

        var replayQuery = Assert.Single(interceptor.Commands, sql =>
            sql.Contains("work_orders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("organization_id", replayQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("environment_id", replayQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("work_order_id", replayQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", replayQuery, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task CreateRushWorkOrderCommand_CreatesHighPriorityWorkOrderWithoutScheduling()
    {
        var store = new InMemoryMesPlanningStore();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        // 前置状态：库里已经存在别的工单与工序。急单在非空库上也必须照样落单——
        // 删掉这两行会把用例输入窄化成「空库建单」，「store 非空则早返」这类实现错误就杀不掉了（#3715 审核 M4）。
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-NORMAL", "SKU-N", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-NORMAL", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2), "SKU-001"));

        var handler = new CreateRushWorkOrderCommandHandler(store);

        var response = await handler.Handle(
            new CreateRushWorkOrderCommand(
                "org-001",
                "env-dev",
                "WO-RUSH",
                "SKU-R",
                "production-version-from-issue-95",
                1m,
                now.AddHours(4),
                "WC-A",
                "OP-RUSH-20",
                20,
                TimeSpan.FromHours(1),
                now),
            CancellationToken.None);

        Assert.Equal("WO-RUSH", response.WorkOrderId);
        Assert.Equal(1000, store.WorkOrders.Single(x => x.WorkOrderId == "WO-RUSH").Priority);
        var operation = Assert.Single(store.OperationTasks, x => x.WorkOrderId == "WO-RUSH");
        Assert.Equal("OP-RUSH-20", operation.OperationTaskId);
        Assert.Equal(20, operation.OperationSequence);
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public void Clear() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task CreateRushWorkOrderCommand_GeneratesWorkOrderAndOperationIdsWhenNotProvided()
    {
        var store = new InMemoryMesPlanningStore();
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new CreateRushWorkOrderCommandHandler(store, numbering);

        var response = await handler.Handle(
            new CreateRushWorkOrderCommand(
                "org-001",
                "env-dev",
                null,
                "SKU-R",
                "production-version-from-issue-188",
                1m,
                now.AddHours(4),
                "WC-A",
                null,
                10,
                TimeSpan.FromHours(1),
                now,
                "rush-create-001"),
            CancellationToken.None);

        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", response.WorkOrderId);
        Assert.Equal(response.WorkOrderId, store.WorkOrders.Single().WorkOrderId);
        Assert.Equal($"{response.WorkOrderId}-OP-10", store.OperationTasks.Single().OperationTaskId);
    }

    [Fact]
    public async Task CreateRushWorkOrderCommand_ReusesExistingWorkOrderForSameIdempotencyKey()
    {
        var store = new InMemoryMesPlanningStore();
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new CreateRushWorkOrderCommandHandler(store, numbering);
        var command = new CreateRushWorkOrderCommand(
            "org-001",
            "env-dev",
            null,
            "SKU-R",
            "production-version-from-issue-188",
            1m,
            now.AddHours(4),
            "WC-A",
            null,
            10,
            TimeSpan.FromHours(1),
            now,
            "rush-create-002");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.WorkOrderId, second.WorkOrderId);
        Assert.Single(store.WorkOrders);
        Assert.Single(store.OperationTasks);
    }

    [Fact]
    public async Task CreateRushWorkOrderCommand_GeneratesUniqueWorkOrdersForParallelRequests()
    {
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");

        var tasks = Enumerable.Range(1, 20)
            .Select(async index =>
            {
                var store = new InMemoryMesPlanningStore();
                var handler = new CreateRushWorkOrderCommandHandler(store, numbering);
                var response = await handler.Handle(
                    new CreateRushWorkOrderCommand(
                        "org-001",
                        "env-dev",
                        null,
                        $"SKU-R-{index}",
                        "production-version-from-issue-188",
                        1m,
                        now.AddHours(4),
                        "WC-A",
                        null,
                        10,
                        TimeSpan.FromHours(1),
                        now,
                        $"rush-parallel-{index}"),
                    CancellationToken.None);
                return response.WorkOrderId;
            });

        var workOrderIds = await Task.WhenAll(tasks);

        Assert.Equal(20, workOrderIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(workOrderIds, id => Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", id));
    }

    [Fact]
    public async Task ConvertPlanToWorkOrderCommand_GeneratesWorkOrderAndReplaysIdempotentResult()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        var handler = new ConvertPlanToWorkOrderCommandHandler(dbContext, numbering);
        var command = new ConvertPlanToWorkOrderCommand(
            "org-001",
            "env-dev",
            "PLAN-001",
            null,
            now,
            "SKU-FG-1000",
            "PV-001",
            10m,
            "PCS",
            now.AddDays(2),
            "WC-A",
            IdempotencyKey:
            "convert-plan-001");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.ReferenceId, second.ReferenceId);
        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", first.ReferenceId);
    }

    [Fact]
    public async Task ConvertPlanToWorkOrderCommand_CreatesOperationForWorkCenterShortcut()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var numbering = new MesCodingService();
        var now = DateTimeOffset.Parse("2026-05-22T08:00:00Z");
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "UNAV-WC-A",
            "WC-A",
            now,
            now.AddHours(2),
            "maintenance",
            "DEV-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ConvertPlanToWorkOrderCommandHandler(dbContext, numbering);

        var response = await handler.Handle(
            new ConvertPlanToWorkOrderCommand(
                "org-001",
                "env-dev",
                "PLAN-001",
                "WO-PLAN-001",
                now,
                "SKU-FG-1000",
                "PV-001",
                10m,
                "PCS",
                now.AddDays(2),
                "WC-A",
                IdempotencyKey: "convert-plan-schedule-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("WO-PLAN-001", response.ReferenceId);
        // WorkCenterId 捷径分支只建工序：工序号由工单号派生，序号固定 10。
        var operation = await dbContext.OperationTasks.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal("WO-PLAN-001", operation.WorkOrderId);
        Assert.Equal("WO-PLAN-001-OP-10", operation.OperationTaskIdValue);
    }

    [Fact]
    public async Task MesCodingService_PersistsCounterAndIdempotencyKey()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var coding = new MesCodingService(dbContext, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var allocation = await coding.AllocateAsync(
            "org-001",
            "env-dev",
            "work-order",
            null,
            "mes-persisted-numbering",
            "payload",
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Matches("^WO-[0-9]{8}-[0-9]{6}$", allocation.Code);
        using var observerScope = provider.CreateScope();
        var observerContext = observerScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        Assert.Single(observerContext.CodeCounters);
        var idempotency = Assert.Single(observerContext.CodeIdempotencyKeys);
        Assert.Equal(allocation.Code, idempotency.Code);
    }
}
