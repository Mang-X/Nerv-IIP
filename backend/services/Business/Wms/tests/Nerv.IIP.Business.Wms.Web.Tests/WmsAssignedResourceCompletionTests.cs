using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsAssignedResourceCompletionTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private const string SiteCode = "SITE-A";
    private const string PoolCode = "POOL-A";
    private const string AssignedOperator = "worker-a";
    private const string OtherOperator = "worker-b";

    [Fact]
    public void Completion_commands_require_trusted_scope_and_expected_version()
    {
        foreach (var commandType in new[]
                 {
                     typeof(CompleteInboundOrderCommand),
                     typeof(CompleteOutboundOrderCommand),
                     typeof(CompleteCountExecutionCommand),
                 })
        {
            Assert.NotNull(commandType.GetProperty("OrganizationId"));
            Assert.NotNull(commandType.GetProperty("EnvironmentId"));
            Assert.NotNull(commandType.GetProperty("ActorPrincipalId"));
            Assert.NotNull(commandType.GetProperty("AuthorizedSiteCodes"));
            Assert.NotNull(commandType.GetProperty("ScopeKind"));
            Assert.NotNull(commandType.GetProperty("ScopeId"));
            Assert.NotNull(commandType.GetProperty("ExpectedVersion"));
        }
    }

    [Fact]
    public async Task Complete_inbound_rejects_cross_site_and_other_operator_before_movement_write()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var crossSite = CreateInbound("IN-CROSS-SITE");
        var otherOperator = CreateInbound("IN-OTHER-OPERATOR");
        dbContext.InboundOrders.AddRange(crossSite, otherOperator);
        await dbContext.SaveChangesAsync();

        var handler = new CompleteInboundOrderCommandHandler(dbContext);
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                CompleteInbound(
                    crossSite,
                    AssignedOperator,
                    ["SITE-B"],
                    "self",
                    AssignedOperator,
                    "inbound-cross-site"),
                CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                CompleteInbound(
                    otherOperator,
                    OtherOperator,
                    [SiteCode],
                    "self",
                    OtherOperator,
                    "inbound-other-operator"),
                CancellationToken.None));

        Assert.Equal(InboundOrderStatus.Open, crossSite.Status);
        Assert.Equal(InboundOrderStatus.Open, otherOperator.Status);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_outbound_rejects_stale_version_before_inventory_release()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var outbound = CreateOutboundWithCompletedShortPick("OUT-STALE");
        dbContext.OutboundOrders.Add(outbound.Order);
        dbContext.WarehouseTasks.Add(outbound.PickingTask);
        await dbContext.SaveChangesAsync();
        var inventory = new RecordingInventoryClient();

        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new CompleteOutboundOrderCommandHandler(dbContext, inventory).Handle(
                CompleteOutbound(
                    outbound.Order,
                    ExpectedVersion: outbound.Order.Version - 1),
                CancellationToken.None));

        Assert.Equal(OutboundOrderStatus.Open, outbound.Order.Status);
        Assert.Empty(inventory.ReleaseRequests);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_outbound_rejects_terminal_resource_before_inventory_release()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var outbound = OutboundOrder.Create(
            OrganizationId,
            EnvironmentId,
            "OUT-TERMINAL",
            "sales-shipment",
            "SO-TERMINAL",
            SiteCode,
            [OutboundLine()],
            AssignedOperator,
            PoolCode);
        outbound.Cancel("cancelled before review");
        dbContext.OutboundOrders.Add(outbound);
        await dbContext.SaveChangesAsync();
        var inventory = new RecordingInventoryClient();

        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new CompleteOutboundOrderCommandHandler(dbContext, inventory).Handle(
                CompleteOutbound(outbound),
                CancellationToken.None));

        Assert.Empty(inventory.ReleaseRequests);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_count_rejects_invalid_scope_before_inventory_confirmation()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var count = CreateCount("COUNT-SCOPE");
        dbContext.CountExecutions.Add(count);
        await dbContext.SaveChangesAsync();
        var inventory = new RecordingInventoryClient();

        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            new CompleteCountExecutionCommandHandler(dbContext, inventory).Handle(
                CompleteCount(
                    count,
                    AssignedOperator,
                    ["SITE-B"],
                    "self",
                    AssignedOperator),
                CancellationToken.None));

        Assert.Equal(CountExecutionStatus.Open, count.Status);
        Assert.Empty(inventory.CountTaskRequests);
        Assert.Empty(inventory.CountAdjustmentRequests);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_count_rejects_stale_version_before_inventory_confirmation()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var count = CreateCount("COUNT-STALE");
        dbContext.CountExecutions.Add(count);
        await dbContext.SaveChangesAsync();
        var inventory = new RecordingInventoryClient();

        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new CompleteCountExecutionCommandHandler(dbContext, inventory).Handle(
                CompleteCount(count) with { ExpectedVersion = count.Version + 1 },
                CancellationToken.None));

        Assert.Equal(CountExecutionStatus.Open, count.Status);
        Assert.Empty(inventory.CountTaskRequests);
        Assert.Empty(inventory.CountAdjustmentRequests);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_inbound_maps_invalid_capture_to_unprocessable_without_movement_write()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var inbound = CreateInbound("IN-INVALID");
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync();
        var command = CompleteInbound(
            inbound,
            AssignedOperator,
            [SiteCode],
            "self",
            AssignedOperator,
            "inbound-invalid") with
        {
            Lines =
            [
                new InboundOrderLineCapture(
                    "10",
                    null,
                    new DateOnly(2026, 7, 2),
                    new DateOnly(2026, 7, 1)),
            ],
        };

        await Assert.ThrowsAsync<WmsUnprocessableException>(() =>
            new CompleteInboundOrderCommandHandler(dbContext).Handle(
                command,
                CancellationToken.None));

        Assert.Equal(InboundOrderStatus.Open, inbound.Status);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_outbound_maps_failed_pack_review_to_unprocessable_without_inventory_release()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var outbound = CreateOutboundWithCompletedShortPick("OUT-INVALID");
        dbContext.OutboundOrders.Add(outbound.Order);
        dbContext.WarehouseTasks.Add(outbound.PickingTask);
        await dbContext.SaveChangesAsync();
        var inventory = new RecordingInventoryClient();

        await Assert.ThrowsAsync<WmsUnprocessableException>(() =>
            new CompleteOutboundOrderCommandHandler(dbContext, inventory).Handle(
                CompleteOutbound(outbound.Order) with { Passed = false },
                CancellationToken.None));

        Assert.Equal(OutboundOrderStatus.Open, outbound.Order.Status);
        Assert.Empty(inventory.ReleaseRequests);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_count_maps_negative_quantity_to_unprocessable_without_inventory_confirmation()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var count = CreateCount("COUNT-INVALID");
        dbContext.CountExecutions.Add(count);
        await dbContext.SaveChangesAsync();
        var inventory = new RecordingInventoryClient();

        await Assert.ThrowsAsync<WmsUnprocessableException>(() =>
            new CompleteCountExecutionCommandHandler(dbContext, inventory).Handle(
                CompleteCount(count) with { CountedQuantity = -1m },
                CancellationToken.None));

        Assert.Equal(CountExecutionStatus.Open, count.Status);
        Assert.Empty(inventory.CountTaskRequests);
        Assert.Empty(inventory.CountAdjustmentRequests);
        Assert.Empty(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_inbound_replay_accepts_original_expected_version_after_authorization()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        var inbound = CreateInbound("IN-REPLAY");
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync();
        var command = CompleteInbound(
            inbound,
            AssignedOperator,
            [SiteCode],
            "self",
            AssignedOperator,
            "inbound-replay");
        var handler = new CompleteInboundOrderCommandHandler(dbContext);

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var replay = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(2, inbound.Version);
        Assert.Single(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    [Fact]
    public async Task Complete_inbound_replay_reauthorizes_before_returning_receipt()
    {
        await using var dbContext = CreateContext();
        var membership = SeedWorkBoundary(dbContext);
        var inbound = CreateInbound("IN-REAUTH");
        dbContext.InboundOrders.Add(inbound);
        await dbContext.SaveChangesAsync();
        var command = CompleteInbound(
            inbound,
            AssignedOperator,
            [SiteCode],
            "self",
            AssignedOperator,
            "inbound-reauth");
        var handler = new CompleteInboundOrderCommandHandler(dbContext);
        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        membership.Deactivate(DateTime.UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Single(await dbContext.InventoryMovementRequests.ToListAsync());
    }

    /// <summary>
    /// #1343：站点范围（IAM 精确站点授权）读写口径一致——站内资源可完成，跨站仍 403。
    /// 写侧闸门若只认作业池成员资格，admin/主管选 site 后每个按钮都会 403。
    /// </summary>
    [Fact]
    public async Task Site_scope_completes_station_wide_resources_without_pool_membership()
    {
        await using var dbContext = CreateContext();
        SeedWorkBoundary(dbContext);
        // 站点主管不是作业池成员；资源派到作业池但未派给具体操作员。
        var inbound = CreatePoolOnlyInbound("IN-SITE-SCOPE");
        var count = CreatePoolOnlyCount("COUNT-SITE-SCOPE");
        dbContext.InboundOrders.Add(inbound);
        dbContext.CountExecutions.Add(count);
        await dbContext.SaveChangesAsync();

        await new CompleteInboundOrderCommandHandler(dbContext).Handle(
            CompleteInbound(
                inbound,
                "user-site-supervisor",
                [SiteCode],
                "site",
                SiteCode,
                "inbound-site-scope"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await new CompleteCountExecutionCommandHandler(dbContext).Handle(
            new CompleteCountExecutionCommand(
                count.Id,
                // 盘点差异非零才会产生库存移动请求；这里验的是授权闸门，不是等量回填。
                8m,
                $"complete-{count.CountNo}",
                OrganizationId,
                EnvironmentId,
                "user-site-supervisor",
                [SiteCode],
                "site",
                SiteCode,
                count.Version),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.NotEqual(InboundOrderStatus.Open, inbound.Status);
        Assert.Equal(CountExecutionStatus.Completed, count.Status);

        // 站点边界仍强制：授权站点之外一律拒绝。
        var crossSite = CreatePoolOnlyInbound("IN-SITE-SCOPE-CROSS");
        dbContext.InboundOrders.Add(crossSite);
        await dbContext.SaveChangesAsync();
        var denied = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            new CompleteInboundOrderCommandHandler(dbContext).Handle(
                CompleteInbound(
                    crossSite,
                    "user-site-supervisor",
                    ["SITE-B"],
                    "site",
                    "SITE-B",
                    "inbound-site-scope-cross"),
                CancellationToken.None));

        Assert.Equal("site-outside-exact-grant", denied.Reason);
        Assert.Equal(InboundOrderStatus.Open, crossSite.Status);
    }

    private static InboundOrder CreatePoolOnlyInbound(string orderNo) =>
        InboundOrder.Create(
            OrganizationId,
            EnvironmentId,
            orderNo,
            "purchase-receipt",
            $"PO-{orderNo}",
            SiteCode,
            [InboundLine()],
            null,
            PoolCode);

    private static CountExecution CreatePoolOnlyCount(string countNo) =>
        CountExecution.Create(
            OrganizationId,
            EnvironmentId,
            countNo,
            "SKU-001",
            "EA",
            SiteCode,
            "BIN-01",
            10m,
            null,
            PoolCode);

    private static CompleteInboundOrderCommand CompleteInbound(
        InboundOrder inbound,
        string actorPrincipalId,
        IReadOnlyCollection<string> authorizedSiteCodes,
        string scopeKind,
        string scopeId,
        string idempotencyKey) =>
        new(
            inbound.Id,
            idempotencyKey,
            OrganizationId: OrganizationId,
            EnvironmentId: EnvironmentId,
            ActorPrincipalId: actorPrincipalId,
            AuthorizedSiteCodes: authorizedSiteCodes,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            ExpectedVersion: inbound.Version);

    private static CompleteOutboundOrderCommand CompleteOutbound(
        OutboundOrder outbound,
        long? ExpectedVersion = null) =>
        new(
            outbound.Id,
            "PACK-001",
            true,
            $"complete-{outbound.OutboundOrderNo}",
            OrganizationId,
            EnvironmentId,
            AssignedOperator,
            [SiteCode],
            "self",
            AssignedOperator,
            ExpectedVersion ?? outbound.Version);

    private static CompleteCountExecutionCommand CompleteCount(
        CountExecution count,
        string actorPrincipalId = AssignedOperator,
        IReadOnlyCollection<string>? authorizedSiteCodes = null,
        string scopeKind = "self",
        string scopeId = AssignedOperator) =>
        new(
            count.Id,
            10m,
            $"complete-{count.CountNo}",
            OrganizationId,
            EnvironmentId,
            actorPrincipalId,
            authorizedSiteCodes ?? [SiteCode],
            scopeKind,
            scopeId,
            count.Version);

    private static InboundOrder CreateInbound(string orderNo) =>
        InboundOrder.Create(
            OrganizationId,
            EnvironmentId,
            orderNo,
            "purchase-receipt",
            $"PO-{orderNo}",
            SiteCode,
            [InboundLine()],
            AssignedOperator,
            PoolCode);

    private static CountExecution CreateCount(string countNo) =>
        CountExecution.Create(
            OrganizationId,
            EnvironmentId,
            countNo,
            "SKU-001",
            "EA",
            SiteCode,
            "BIN-01",
            10m,
            AssignedOperator,
            PoolCode);

    private static OutboundWithTask CreateOutboundWithCompletedShortPick(string orderNo)
    {
        var outbound = OutboundOrder.Create(
            OrganizationId,
            EnvironmentId,
            orderNo,
            "sales-shipment",
            $"SO-{orderNo}",
            SiteCode,
            [OutboundLine()],
            AssignedOperator,
            PoolCode);
        var pickingTask = outbound.CreatePickingTask(
            $"PICK-{orderNo}",
            "10",
            "BIN-01",
            "PACK-01",
            10m,
            "reservation-001",
            "BIN-01",
            null,
            null,
            AssignedOperator,
            PoolCode);
        pickingTask.Start(AssignedOperator, pickingTask.Version);
        pickingTask.Complete(8m, AssignedOperator, "short pick", pickingTask.Version);
        return new OutboundWithTask(outbound, pickingTask);
    }

    private static InboundOrderLineDraft InboundLine() =>
        new(
            "10",
            "SKU-001",
            "EA",
            10m,
            "RECEIVING-01",
            null,
            null,
            "qualified",
            "company",
            null);

    private static OutboundOrderLineDraft OutboundLine() =>
        new(
            "10",
            "SKU-001",
            "EA",
            10m,
            "BIN-01",
            null,
            null,
            "qualified",
            "company",
            null);

    private static WarehouseWorkPoolMembership SeedWorkBoundary(
        ApplicationDbContext dbContext)
    {
        dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            OrganizationId,
            EnvironmentId,
            PoolCode,
            "仓储作业池",
            SiteCode));
        var assignedMembership = WarehouseWorkPoolMembership.Create(
            OrganizationId,
            EnvironmentId,
            PoolCode,
            AssignedOperator,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(1));
        dbContext.WarehouseWorkPoolMemberships.AddRange(
            assignedMembership,
            WarehouseWorkPoolMembership.Create(
                OrganizationId,
                EnvironmentId,
                PoolCode,
                OtherOperator,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(1)));
        return assignedMembership;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-assigned-completion-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed record OutboundWithTask(
        OutboundOrder Order,
        WarehouseTask PickingTask);

    private sealed class RecordingInventoryClient : IWmsInventoryReservationClient
    {
        public List<WmsInventoryReservationReleaseRequest> ReleaseRequests { get; } = [];
        public List<WmsInventoryCountTaskRequest> CountTaskRequests { get; } = [];
        public List<WmsInventoryCountAdjustmentRequest> CountAdjustmentRequests { get; } = [];

        public Task<WmsInventoryReservationResult> ReserveAsync(
            WmsInventoryReservationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WmsInventoryFefoReservationResult> ReserveFefoAsync(
            WmsInventoryFefoReservationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
            WmsInventoryReservationReleaseRequest request,
            CancellationToken cancellationToken)
        {
            ReleaseRequests.Add(request);
            return Task.FromResult(new WmsInventoryReservationReleaseResult(
                request.ReservationId,
                0m,
                request.Quantity));
        }

        public Task<WmsInventoryReservationRenewalResult> RenewAsync(
            WmsInventoryReservationRenewalRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WmsInventoryCountTaskResult> CreateCountTaskAsync(
            WmsInventoryCountTaskRequest request,
            CancellationToken cancellationToken)
        {
            CountTaskRequests.Add(request);
            return Task.FromResult(new WmsInventoryCountTaskResult(
                "count-task-001",
                1));
        }

        public Task<WmsInventoryCountAdjustmentResult> ConfirmCountAdjustmentAsync(
            WmsInventoryCountAdjustmentRequest request,
            CancellationToken cancellationToken)
        {
            CountAdjustmentRequests.Add(request);
            return Task.FromResult(new WmsInventoryCountAdjustmentResult(
                "movement-001",
                request.CountedQuantity - 10m,
                request.CountedQuantity));
        }
    }
}
