using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WarehouseAssignmentCommandTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Five_assignment_routes_are_typed_and_create_dtos_cannot_assign()
    {
        var expected = new[]
        {
            (typeof(AssignInboundOrderEndpoint), "/api/business/v1/wms/inbound-orders/{inboundOrderId}/assignment", WmsPermissionCodes.ReceiptsManage),
            (typeof(AssignPutawayTaskEndpoint), "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/assignment", WmsPermissionCodes.ReceiptsManage),
            (typeof(AssignOutboundOrderEndpoint), "/api/business/v1/wms/outbound-orders/{outboundOrderId}/assignment", WmsPermissionCodes.ShipmentsManage),
            (typeof(AssignPickingTaskEndpoint), "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/assignment", WmsPermissionCodes.ShipmentsManage),
            (typeof(AssignCountExecutionEndpoint), "/api/business/v1/wms/count-executions/{countExecutionId}/assignment", WmsPermissionCodes.ReceiptsManage),
        };
        foreach (var (endpointType, route, permission) in expected)
        {
            var contract = Assert.Single(
                WmsEndpointContracts.All,
                candidate => candidate.EndpointType == endpointType);
            Assert.Equal("POST", contract.HttpMethod);
            Assert.Equal(route, contract.Route);
            Assert.Equal(permission, contract.PermissionCode);
            Assert.Equal(InternalServiceAuthorizationPolicy.Name, contract.AuthorizationPolicy);
        }

        foreach (var createRequestType in new[]
                 {
                     typeof(CreateInboundOrderRequest),
                     typeof(CreatePutawayTaskRequest),
                     typeof(CreateOutboundOrderRequest),
                     typeof(CreatePickingTaskRequest),
                     typeof(CreateCountExecutionRequest),
                 })
        {
            Assert.Null(createRequestType.GetProperty("AssignedOperatorUserId"));
            Assert.Null(createRequestType.GetProperty("AssignedPoolCode"));
        }
    }

    [Fact]
    public async Task Five_assignment_categories_persist_pool_operator_and_authoritative_version()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedAssignmentBoundary(dbContext);
        var resources = SeedResources(dbContext);
        await dbContext.SaveChangesAsync();
        var authorizer = Authorizer(dbContext);

        var inbound = await new AssignInboundOrderCommandHandler(dbContext, authorizer).Handle(
            Command.ForInbound(resources.Inbound.Id),
            CancellationToken.None);
        var putaway = await new AssignPutawayTaskCommandHandler(dbContext, authorizer).Handle(
            Command.ForPutaway(resources.Putaway.Id),
            CancellationToken.None);
        var outbound = await new AssignOutboundOrderCommandHandler(dbContext, authorizer).Handle(
            Command.ForOutbound(resources.Outbound.Id),
            CancellationToken.None);
        var picking = await new AssignPickingTaskCommandHandler(dbContext, authorizer).Handle(
            Command.ForPicking(resources.Picking.Id),
            CancellationToken.None);
        var count = await new AssignCountExecutionCommandHandler(dbContext, authorizer).Handle(
            Command.ForCount(resources.Count.Id),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.All(
            new[] { inbound, putaway, outbound, picking, count },
            result =>
            {
                Assert.Equal("SITE-001", result.SiteCode);
                Assert.Equal("POOL-WAREHOUSE", result.PoolCode);
                Assert.Equal("user-target", result.OperatorPrincipalId);
                Assert.Equal("user-manager", result.AssignedByPrincipalId);
                Assert.Equal(2, result.Version);
            });
        Assert.Equal("POOL-WAREHOUSE", resources.Inbound.AssignedPoolCode);
        Assert.Equal("POOL-WAREHOUSE", resources.Outbound.AssignedPoolCode);
        Assert.Equal("POOL-WAREHOUSE", resources.Putaway.AssignedPoolCode);
        Assert.Equal("POOL-WAREHOUSE", resources.Picking.AssignedPoolCode);
        Assert.Equal("POOL-WAREHOUSE", resources.Count.AssignedPoolCode);
        Assert.Equal(5, await dbContext.WarehouseAssignmentReceipts.CountAsync());
    }

    [Fact]
    public async Task Same_assignment_intent_replays_receipt_but_changed_payload_conflicts()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedAssignmentBoundary(dbContext);
        var resources = SeedResources(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new AssignPickingTaskCommandHandler(dbContext, Authorizer(dbContext));
        var command = Command.ForPicking(resources.Picking.Id);

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var persistedReceipt = Assert.Single(
            await dbContext.WarehouseAssignmentReceipts.AsNoTracking().ToListAsync());
        Assert.Equal(resources.Picking.Id.ToString(), persistedReceipt.ResourceId);
        Assert.Equal(
            WmsText.IdempotencyKey(command.IdempotencyKey),
            persistedReceipt.IdempotencyKey);
        var replay = await handler.Handle(
            command with
            {
                AuthorizedSiteCodes = ["SITE-002", "SITE-001"],
            },
            CancellationToken.None);

        Assert.Equal(first, replay);
        var forbidden = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            handler.Handle(
                command with { AuthorizedSiteCodes = ["SITE-002"] },
                CancellationToken.None));
        Assert.Equal("site-outside-exact-grant", forbidden.Reason);
        await Assert.ThrowsAsync<WmsIdempotencyConflictException>(() =>
            handler.Handle(
                command with { OperatorPrincipalId = null },
                CancellationToken.None));
        Assert.Single(await dbContext.WarehouseAssignmentReceipts.ToListAsync());
    }

    [Fact]
    public async Task Assignment_rejects_cross_site_non_member_stale_and_terminal_resources()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedAssignmentBoundary(dbContext);
        var resources = SeedResources(dbContext);
        await dbContext.SaveChangesAsync();
        var authorizer = Authorizer(dbContext);

        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            new AssignInboundOrderCommandHandler(dbContext, authorizer).Handle(
                Command.ForInbound(resources.Inbound.Id) with
                {
                    AuthorizedSiteCodes = ["SITE-002"],
                },
                CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            new AssignPickingTaskCommandHandler(dbContext, authorizer).Handle(
                Command.ForPicking(resources.Picking.Id) with
                {
                    OperatorPrincipalId = "user-nonmember",
                },
                CancellationToken.None));
        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new AssignOutboundOrderCommandHandler(dbContext, authorizer).Handle(
                Command.ForOutbound(resources.Outbound.Id) with { ExpectedVersion = 99 },
                CancellationToken.None));

        resources.Putaway.Assign("POOL-WAREHOUSE", "user-target", 1);
        resources.Putaway.Start("user-target", 2);
        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new AssignPutawayTaskCommandHandler(dbContext, authorizer).Handle(
                Command.ForPutaway(resources.Putaway.Id) with { ExpectedVersion = 3 },
                CancellationToken.None));
    }

    private static WarehouseWorkScopeAuthorizer Authorizer(ApplicationDbContext dbContext) =>
        new(dbContext, new StaticTimeProvider(Now));

    private static void SeedAssignmentBoundary(ApplicationDbContext dbContext)
    {
        dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            "org-001",
            "env-dev",
            "POOL-WAREHOUSE",
            "仓储作业池",
            "SITE-001"));
        dbContext.WarehouseWorkPoolMemberships.Add(WarehouseWorkPoolMembership.Create(
            "org-001",
            "env-dev",
            "POOL-WAREHOUSE",
            "user-target",
            Now.AddDays(-1),
            Now.AddDays(1)));
    }

    private static AssignmentResources SeedResources(ApplicationDbContext dbContext)
    {
        var inbound = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-order",
            "PO-001",
            "SITE-001",
            [
                new InboundOrderLineDraft(
                    "10",
                    "SKU-001",
                    "pcs",
                    5m,
                    "RECEIVING-01",
                    null,
                    null,
                    "qualified",
                    "company",
                    null),
            ]);
        var putaway = WarehouseTask.CreatePutaway(
            "org-001",
            "env-dev",
            "PUT-001",
            "IN-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-001",
            "RECEIVING-01",
            "BIN-01",
            5m);
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-order",
            "SO-001",
            "SITE-001",
            [
                new OutboundOrderLineDraft(
                    "10",
                    "SKU-001",
                    "pcs",
                    5m,
                    "BIN-01",
                    null,
                    null,
                    "qualified",
                    "company",
                    null),
            ]);
        var picking = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "PICK-001",
            "OUT-001",
            "10",
            "SKU-001",
            "pcs",
            "SITE-001",
            "BIN-01",
            "PACK-01",
            5m);
        var count = CountExecution.Create(
            "org-001",
            "env-dev",
            "COUNT-001",
            "SKU-001",
            "pcs",
            "SITE-001",
            "BIN-01",
            5m);
        dbContext.AddRange(inbound, putaway, outbound, picking, count);
        return new AssignmentResources(inbound, putaway, outbound, picking, count);
    }

    private sealed record AssignmentResources(
        InboundOrder Inbound,
        WarehouseTask Putaway,
        OutboundOrder Outbound,
        WarehouseTask Picking,
        CountExecution Count);

    private static class Command
    {
        public static AssignInboundOrderCommand ForInbound(InboundOrderId id) =>
            new(
                id,
                "org-001",
                "env-dev",
                "user-manager",
                ["SITE-001"],
                "POOL-WAREHOUSE",
                "user-target",
                "assign-inbound",
                1);

        public static AssignPutawayTaskCommand ForPutaway(WarehouseTaskId id) =>
            new(
                id,
                "org-001",
                "env-dev",
                "user-manager",
                ["SITE-001"],
                "POOL-WAREHOUSE",
                "user-target",
                "assign-putaway",
                1);

        public static AssignOutboundOrderCommand ForOutbound(OutboundOrderId id) =>
            new(
                id,
                "org-001",
                "env-dev",
                "user-manager",
                ["SITE-001"],
                "POOL-WAREHOUSE",
                "user-target",
                "assign-outbound",
                1);

        public static AssignPickingTaskCommand ForPicking(WarehouseTaskId id) =>
            new(
                id,
                "org-001",
                "env-dev",
                "user-manager",
                ["SITE-001"],
                "POOL-WAREHOUSE",
                "user-target",
                "assign-picking",
                1);

        public static AssignCountExecutionCommand ForCount(CountExecutionId id) =>
            new(
                id,
                "org-001",
                "env-dev",
                "user-manager",
                ["SITE-001"],
                "POOL-WAREHOUSE",
                "user-target",
                "assign-count",
                1);
    }

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow, TimeSpan.Zero);
    }
}
