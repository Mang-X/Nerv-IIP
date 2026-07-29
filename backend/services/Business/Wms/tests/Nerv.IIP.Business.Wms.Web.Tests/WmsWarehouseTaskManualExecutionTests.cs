using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsWarehouseTaskManualExecutionTests
{
    [Fact]
    public void Manual_putaway_and_picking_actions_have_typed_internal_service_contracts()
    {
        var expected = new[]
        {
            new ExpectedContract(
                "StartPutawayTaskEndpoint",
                "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/start",
                WmsPermissionCodes.ReceiptsManage,
                "startWmsPutawayTask"),
            new ExpectedContract(
                "RecordPutawayTaskProgressEndpoint",
                "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/progress",
                WmsPermissionCodes.ReceiptsManage,
                "recordWmsPutawayTaskProgress"),
            new ExpectedContract(
                "ReportPutawayTaskExceptionEndpoint",
                "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/exception",
                WmsPermissionCodes.ReceiptsManage,
                "reportWmsPutawayTaskException"),
            new ExpectedContract(
                "CompletePutawayTaskEndpoint",
                "/api/business/v1/wms/putaway-tasks/{warehouseTaskId}/complete",
                WmsPermissionCodes.ReceiptsManage,
                "completeWmsPutawayTask"),
            new ExpectedContract(
                "StartPickingTaskEndpoint",
                "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/start",
                WmsPermissionCodes.ShipmentsManage,
                "startWmsPickingTask"),
            new ExpectedContract(
                "RecordPickingTaskProgressEndpoint",
                "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/progress",
                WmsPermissionCodes.ShipmentsManage,
                "recordWmsPickingTaskProgress"),
            new ExpectedContract(
                "ReportPickingTaskExceptionEndpoint",
                "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/exception",
                WmsPermissionCodes.ShipmentsManage,
                "reportWmsPickingTaskException"),
            new ExpectedContract(
                "CompletePickingTaskEndpoint",
                "/api/business/v1/wms/picking-tasks/{warehouseTaskId}/complete",
                WmsPermissionCodes.ShipmentsManage,
                "completeWmsPickingTask"),
        };

        foreach (var item in expected)
        {
            var contract = Assert.Single(
                WmsEndpointContracts.All,
                contract => contract.Route == item.Route);
            Assert.Equal("POST", contract.HttpMethod);
            Assert.Equal(item.EndpointTypeName, contract.EndpointType.Name);
            Assert.Equal(item.PermissionCode, contract.PermissionCode);
            Assert.Equal(InternalServiceAuthorizationPolicy.Name, contract.AuthorizationPolicy);
            Assert.Equal(item.OperationId, contract.OperationId);
        }
    }

    [Fact]
    public async Task Start_action_persists_and_replays_the_same_authoritative_receipt()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-001",
            assignedOperatorUserId: "user-001");
        GrantPoolAccess(dbContext, "user-001");
        dbContext.WarehouseTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var handler = new StartWarehouseTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext));
        var command = new StartWarehouseTaskCommand(
            task.Id,
            "org-001",
            "env-dev",
            "user-001",
            "start-pick-001",
            1,
            WarehouseTaskType.Picking,
            ["SITE-01"],
            "self",
            "user-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var replay = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(WarehouseTaskStatus.InProgress.ToString(), first.Status);
        Assert.Equal(2, first.Version);
        Assert.Equal(first with { AllowedActions = replay.AllowedActions, BlockReasons = replay.BlockReasons }, replay);
        Assert.Single(await dbContext.WarehouseTaskActionReceipts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Reusing_an_action_key_with_a_different_payload_is_a_conflict()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-002",
            assignedOperatorUserId: "user-001");
        GrantPoolAccess(dbContext, "user-001");
        dbContext.WarehouseTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var handler = new StartWarehouseTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext));
        await handler.Handle(
            new StartWarehouseTaskCommand(
                task.Id,
                "org-001",
                "env-dev",
                "user-001",
                "start-pick-002",
                1,
                WarehouseTaskType.Picking,
                ["SITE-01"],
                "self",
                "user-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<WmsIdempotencyConflictException>(() => handler.Handle(
            new StartWarehouseTaskCommand(
                task.Id,
                "org-001",
                "env-dev",
                "user-001",
                "start-pick-002",
                2,
                WarehouseTaskType.Picking,
                ["SITE-01"],
                "self",
                "user-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Personal_assignment_cannot_be_bypassed_by_organization_scope()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = CreateTask(
            WarehouseTaskType.Putaway,
            taskNo: "PUT-001",
            assignedOperatorUserId: "user-owner");
        GrantPoolAccess(dbContext, "user-other");
        dbContext.WarehouseTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            new StartWarehouseTaskCommandHandler(
                dbContext,
                CreateAuthorizer(dbContext)).Handle(
                new StartWarehouseTaskCommand(
                    task.Id,
                    "org-001",
                    "env-dev",
                    "user-other",
                    "start-put-001",
                    1,
                    WarehouseTaskType.Putaway,
                    ["SITE-01"],
                    "work-pool",
                    "POOL-A"),
                CancellationToken.None));

        Assert.Equal("assignment-principal-mismatch", exception.Reason);
        Assert.Equal(WarehouseTaskStatus.Open, task.Status);
    }

    [Fact]
    public async Task Picking_difference_completes_without_inflating_actual_quantity()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-SHORT-001",
            assignedPoolCode: "POOL-A");
        GrantPoolAccess(dbContext, "user-001");
        dbContext.WarehouseTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var start = await new StartWarehouseTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            new StartWarehouseTaskCommand(
                task.Id,
                "org-001",
                "env-dev",
                "user-001",
                "start-short-001",
                1,
                WarehouseTaskType.Picking,
                ["SITE-01"],
                "work-pool",
                "POOL-A"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var completed = await new CompleteWarehouseTaskActionCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            new CompleteWarehouseTaskActionCommand(
                task.Id,
                "org-001",
                "env-dev",
                "user-001",
                "complete-short-001",
                start.Version,
                8m,
                "库位库存不足",
                WarehouseTaskType.Picking,
                ["SITE-01"],
                "self",
                "user-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(WarehouseTaskStatus.CompletedWithDifference.ToString(), completed.Status);
        Assert.Equal(8m, completed.ExecutedQuantity);
        Assert.Equal(2m, completed.DifferenceQuantity);
        Assert.Empty(completed.AllowedActions);
        Assert.Equal(8m, task.ExecutedQuantity);
    }

    [Fact]
    public async Task Putaway_partial_completion_is_a_lifecycle_conflict()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = CreateTask(
            WarehouseTaskType.Putaway,
            taskNo: "PUT-PARTIAL-001",
            assignedOperatorUserId: "user-001");
        GrantPoolAccess(dbContext, "user-001");
        task.Start("user-001", 1);
        dbContext.WarehouseTasks.Add(task);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() =>
            new CompleteWarehouseTaskActionCommandHandler(
                dbContext,
                CreateAuthorizer(dbContext)).Handle(
                new CompleteWarehouseTaskActionCommand(
                    task.Id,
                    "org-001",
                    "env-dev",
                    "user-001",
                    "complete-put-partial-001",
                    2,
                    8m,
                    "少上架",
                    WarehouseTaskType.Putaway,
                    ["SITE-01"],
                    "self",
                    "user-001"),
                CancellationToken.None));

        Assert.Equal(WarehouseTaskStatus.InProgress, task.Status);
        Assert.Equal(0m, task.ExecutedQuantity);
    }

    [Fact]
    public async Task Warehouse_task_list_is_deny_all_without_internal_ownership_scope()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WarehouseTasks.AddRange(
            CreateTask(
                WarehouseTaskType.Picking,
                taskNo: "PICK-SELF",
                lotNo: "LOT-SELF",
                assignedOperatorUserId: "user-001"),
            CreateTask(
                WarehouseTaskType.Picking,
                taskNo: "PICK-TEAM",
                lotNo: "LOT-TEAM",
                assignedPoolCode: "POOL-A"),
            CreateTask(
                WarehouseTaskType.Picking,
                taskNo: "PICK-SITE",
                lotNo: "LOT-SITE"));
        await dbContext.SaveChangesAsync();
        var handler = new ListWarehouseTasksQueryHandler(dbContext);

        var denied = await handler.Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking),
            CancellationToken.None);
        var team = await handler.Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking,
                LotNo: "LOT-TEAM",
                AssignedPoolCodes: ["POOL-A"]),
            CancellationToken.None);
        var organization = await handler.Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking,
                OrganizationWideScope: true),
            CancellationToken.None);
        var ambiguous = await handler.Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking,
                AssignedOperatorUserIds: ["user-001"],
                OrganizationWideScope: true),
            CancellationToken.None);
        var conflicting = await handler.Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking,
                AssignedOperatorUserIds: ["user-001"],
                AssignedPoolCodes: ["POOL-A"]),
            CancellationToken.None);

        Assert.Empty(denied.Items);
        Assert.Equal("PICK-TEAM", Assert.Single(team.Items).TaskNo);
        Assert.Empty(organization.Items);
        Assert.Empty(ambiguous.Items);
        Assert.Empty(conflicting.Items);
    }

    private static WarehouseTask CreateTask(
        WarehouseTaskType taskType,
        string taskNo,
        string? lotNo = null,
        string? assignedOperatorUserId = null,
        string? assignedPoolCode = null)
    {
        const decimal plannedQuantity = 10m;
        var effectivePoolCode = assignedPoolCode
            ?? (assignedOperatorUserId is null ? null : "POOL-A");
        return taskType switch
        {
            WarehouseTaskType.Putaway => WarehouseTask.CreatePutaway(
                "org-001",
                "env-dev",
                taskNo,
                "IN-001",
                "LINE-001",
                "SKU-001",
                "pcs",
                "SITE-01",
                "RECEIVING-01",
                "BIN-01",
                plannedQuantity,
                lotNo,
                null,
                assignedOperatorUserId,
                effectivePoolCode),
            WarehouseTaskType.Picking => WarehouseTask.CreatePicking(
                "org-001",
                "env-dev",
                taskNo,
                "OUT-001",
                "LINE-001",
                "SKU-001",
                "pcs",
                "SITE-01",
                "BIN-01",
                "PACK-01",
                plannedQuantity,
                lotNo,
                null,
                assignedOperatorUserId,
                effectivePoolCode),
            _ => throw new ArgumentOutOfRangeException(nameof(taskType)),
        };
    }

    private static WarehouseWorkScopeAuthorizer CreateAuthorizer(
        ApplicationDbContext dbContext) =>
        new(dbContext, TimeProvider.System);

    private static void GrantPoolAccess(
        ApplicationDbContext dbContext,
        string principalId)
    {
        if (!dbContext.WarehouseWorkPools.Local.Any(x => x.PoolCode == "POOL-A"))
        {
            dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
                "org-001",
                "env-dev",
                "POOL-A",
                "测试作业池",
                "SITE-01"));
        }

        dbContext.WarehouseWorkPoolMemberships.Add(
            WarehouseWorkPoolMembership.Create(
                "org-001",
                "env-dev",
                "POOL-A",
                principalId,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(1)));
    }

    private sealed record ExpectedContract(
        string EndpointTypeName,
        string Route,
        string PermissionCode,
        string OperationId);
}
