using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
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

[Collection(WebApplicationFactoryCollection.Name)]
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
    public void Warehouse_task_action_receipt_recovery_wraps_unit_of_work_save()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        using var scope = factory.Services.CreateScope();

        var behaviorTypes = scope.ServiceProvider
            .GetServices<IPipelineBehavior<StartWarehouseTaskCommand, WarehouseTaskActionResult>>()
            .Select(behavior => behavior.GetType())
            .ToArray();
        var recoveryBehaviorIndex = Array.FindIndex(
            behaviorTypes,
            type => type.IsGenericType
                && type.GetGenericTypeDefinition()
                    == typeof(WarehouseTaskActionReceiptRecoveryBehavior<,>));
        var unitOfWorkBehaviorIndex = Array.FindIndex(
            behaviorTypes,
            type => type.FullName?.Contains("UnitOfWorkBehavior", StringComparison.Ordinal) is true);

        Assert.True(recoveryBehaviorIndex >= 0, "Action-receipt recovery behavior must be registered.");
        Assert.True(unitOfWorkBehaviorIndex >= 0, "Unit-of-work behavior must be registered.");
        Assert.True(
            recoveryBehaviorIndex < unitOfWorkBehaviorIndex,
            "Action-receipt recovery behavior must wrap the unit-of-work save.");
    }

    [Fact]
    public async Task Concurrent_same_payload_replays_the_winning_action_receipt()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"wms-action-receipt-same-{Guid.CreateVersion7():N}";
        await using var losingDbContext =
            CreateConcurrentContext(databaseName, databaseRoot);
        var task = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-RACE-SAME",
            assignedOperatorUserId: "user-001");
        GrantPoolAccess(losingDbContext, "user-001");
        losingDbContext.WarehouseTasks.Add(task);
        await losingDbContext.SaveChangesAsync();
        var command = new StartWarehouseTaskCommand(
            task.Id,
            "org-001",
            "env-dev",
            "user-001",
            "start-race-same",
            1,
            WarehouseTaskType.Picking,
            ["SITE-01"],
            "self",
            "user-001");
        var losingHandler = new StartWarehouseTaskCommandHandler(
            losingDbContext,
            CreateAuthorizer(losingDbContext));
        var behavior =
            new WarehouseTaskActionReceiptRecoveryBehavior<
                StartWarehouseTaskCommand,
                WarehouseTaskActionResult>(losingDbContext);
        var attempts = 0;

        var result = await behavior.Handle(
            command,
            async cancellationToken =>
            {
                attempts++;
                if (attempts == 1)
                {
                    await losingHandler.Handle(command, cancellationToken);
                    await using var winningDbContext =
                        CreateConcurrentContext(databaseName, databaseRoot);
                    await new StartWarehouseTaskCommandHandler(
                            winningDbContext,
                            CreateAuthorizer(winningDbContext))
                        .Handle(command, cancellationToken);
                    await winningDbContext.SaveChangesAsync(cancellationToken);
                    throw ActionReceiptUniqueConflict(losingDbContext);
                }

                return await losingHandler.Handle(command, cancellationToken);
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(WarehouseTaskStatus.InProgress.ToString(), result.Status);
        Assert.Equal(2, result.Version);
        Assert.Single(
            await losingDbContext.WarehouseTaskActionReceipts
                .AsNoTracking()
                .ToListAsync());
    }

    [Fact]
    public async Task Concurrent_different_payload_retries_as_an_idempotency_conflict()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"wms-action-receipt-different-{Guid.CreateVersion7():N}";
        await using var losingDbContext =
            CreateConcurrentContext(databaseName, databaseRoot);
        var task = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-RACE-DIFFERENT",
            assignedOperatorUserId: "user-001");
        task.Start("user-001", task.Version);
        GrantPoolAccess(losingDbContext, "user-001");
        losingDbContext.WarehouseTasks.Add(task);
        await losingDbContext.SaveChangesAsync();
        var losingCommand = new RecordWarehouseTaskProgressActionCommand(
            task.Id,
            "org-001",
            "env-dev",
            "user-001",
            "progress-race-different",
            2,
            6m,
            WarehouseTaskType.Picking,
            ["SITE-01"],
            "self",
            "user-001");
        var winningCommand = losingCommand with { ExecutedQuantity = 5m };
        var losingHandler = new RecordWarehouseTaskProgressActionCommandHandler(
            losingDbContext,
            CreateAuthorizer(losingDbContext));
        var behavior =
            new WarehouseTaskActionReceiptRecoveryBehavior<
                RecordWarehouseTaskProgressActionCommand,
                WarehouseTaskActionResult>(losingDbContext);
        var attempts = 0;

        await Assert.ThrowsAsync<WmsIdempotencyConflictException>(() => behavior.Handle(
            losingCommand,
            async cancellationToken =>
            {
                attempts++;
                if (attempts == 1)
                {
                    await losingHandler.Handle(losingCommand, cancellationToken);
                    await using var winningDbContext =
                        CreateConcurrentContext(databaseName, databaseRoot);
                    await new RecordWarehouseTaskProgressActionCommandHandler(
                            winningDbContext,
                            CreateAuthorizer(winningDbContext))
                        .Handle(winningCommand, cancellationToken);
                    await winningDbContext.SaveChangesAsync(cancellationToken);
                    throw ActionReceiptUniqueConflict(losingDbContext);
                }

                return await losingHandler.Handle(losingCommand, cancellationToken);
            },
            CancellationToken.None));

        Assert.Equal(2, attempts);
        var receipt = Assert.Single(
            await losingDbContext.WarehouseTaskActionReceipts
                .AsNoTracking()
                .ToListAsync());
        Assert.Equal(5m, receipt.ResultExecutedQuantity);
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

    /// <summary>
    /// #1343：站点范围（IAM 精确站点授权）读写口径必须一致。写侧闸门若仍按作业池成员资格
    /// 判定，站点范围就是「能读不能做」——六页每个按钮 403。站点边界仍强制。
    /// </summary>
    [Fact]
    public async Task Site_scope_executes_station_wide_tasks_without_pool_membership_but_never_cross_site()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var putaway = CreateTask(
            WarehouseTaskType.Putaway,
            taskNo: "PUT-SITE-001",
            assignedPoolCode: "POOL-A");
        var picking = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-SITE-001",
            assignedPoolCode: "POOL-A");
        // 站点主管不是任何作业池成员，只有 SITE-01 的精确站点授权。
        GrantPoolAccess(dbContext, "user-operator");
        dbContext.WarehouseTasks.AddRange(putaway, picking);
        await dbContext.SaveChangesAsync();

        var startedPutaway = await new StartWarehouseTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            new StartWarehouseTaskCommand(
                putaway.Id,
                "org-001",
                "env-dev",
                "user-site-supervisor",
                "start-put-site-001",
                1,
                WarehouseTaskType.Putaway,
                ["SITE-01"],
                "site",
                "SITE-01"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var completedPutaway = await new CompleteWarehouseTaskActionCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            new CompleteWarehouseTaskActionCommand(
                putaway.Id,
                "org-001",
                "env-dev",
                "user-site-supervisor",
                "complete-put-site-001",
                startedPutaway.Version,
                10m,
                null,
                WarehouseTaskType.Putaway,
                ["SITE-01"],
                "site",
                "SITE-01"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var startedPicking = await new StartWarehouseTaskCommandHandler(
            dbContext,
            CreateAuthorizer(dbContext)).Handle(
            new StartWarehouseTaskCommand(
                picking.Id,
                "org-001",
                "env-dev",
                "user-site-supervisor",
                "start-pick-site-001",
                1,
                WarehouseTaskType.Picking,
                ["SITE-01"],
                "site",
                "SITE-01"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(WarehouseTaskStatus.InProgress.ToString(), startedPutaway.Status);
        Assert.Equal(WarehouseTaskStatus.Completed.ToString(), completedPutaway.Status);
        Assert.Equal(WarehouseTaskStatus.InProgress.ToString(), startedPicking.Status);

        // 站点边界仍强制：授权站点之外的站点范围一律拒绝，不因 SiteWide 放行。
        var crossSite = await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            new StartWarehouseTaskCommandHandler(
                dbContext,
                CreateAuthorizer(dbContext)).Handle(
                new StartWarehouseTaskCommand(
                    picking.Id,
                    "org-001",
                    "env-dev",
                    "user-site-supervisor",
                    "start-pick-site-cross",
                    startedPicking.Version,
                    WarehouseTaskType.Picking,
                    ["SITE-02"],
                    "site",
                    "SITE-02"),
                CancellationToken.None));

        Assert.Equal("site-outside-exact-grant", crossSite.Reason);
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
    public async Task Warehouse_task_list_actions_respect_actor_assignment_and_execution_channel()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var manual = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-MANUAL",
            assignedOperatorUserId: "user-001");
        manual.Start("user-001", manual.Version);
        var wcs = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-WCS",
            assignedPoolCode: "POOL-A");
        wcs.ClaimWcsExecution("wcs-task-001", wcs.Version);
        var terminal = CreateTask(
            WarehouseTaskType.Picking,
            taskNo: "PICK-TERMINAL",
            assignedOperatorUserId: "user-001");
        terminal.Start("user-001", terminal.Version);
        terminal.Complete(terminal.PlannedQuantity, "user-001", null, terminal.Version);
        dbContext.WarehouseTasks.AddRange(
            CreateTask(
                WarehouseTaskType.Picking,
                taskNo: "PICK-SELF",
                assignedOperatorUserId: "user-001"),
            CreateTask(
                WarehouseTaskType.Picking,
                taskNo: "PICK-POOL",
                assignedPoolCode: "POOL-A"),
            CreateTask(
                WarehouseTaskType.Picking,
                taskNo: "PICK-OTHER",
                assignedOperatorUserId: "user-other"),
            manual,
            wcs,
            terminal);
        await dbContext.SaveChangesAsync();

        var result = await new ListWarehouseTasksQueryHandler(dbContext).Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking,
                AssignedPoolCodes: ["POOL-A"],
                ActorPrincipalId: "user-001"),
            CancellationToken.None);
        var rows = result.Items.ToDictionary(x => x.TaskNo, StringComparer.Ordinal);

        Assert.Equal(["start"], rows["PICK-SELF"].AllowedActions);
        Assert.Empty(rows["PICK-SELF"].BlockReasons);
        Assert.Equal(["start"], rows["PICK-POOL"].AllowedActions);
        Assert.Empty(rows["PICK-POOL"].BlockReasons);
        Assert.Empty(rows["PICK-OTHER"].AllowedActions);
        Assert.Equal(
            ["TASK_ASSIGNED_TO_ANOTHER_OPERATOR"],
            rows["PICK-OTHER"].BlockReasons);
        Assert.Equal(
            ["progress", "exception", "complete"],
            rows["PICK-MANUAL"].AllowedActions);
        Assert.Empty(rows["PICK-MANUAL"].BlockReasons);
        Assert.Empty(rows["PICK-WCS"].AllowedActions);
        Assert.Equal(
            ["TASK_EXECUTION_CLAIMED_BY_WCS"],
            rows["PICK-WCS"].BlockReasons);
        Assert.Empty(rows["PICK-TERMINAL"].AllowedActions);
        Assert.Equal(["TASK_TERMINAL"], rows["PICK-TERMINAL"].BlockReasons);
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

    private static DbUpdateException ActionReceiptUniqueConflict(
        ApplicationDbContext dbContext)
    {
        var constraintName = dbContext.Model.FindEntityType(typeof(WarehouseTaskActionReceipt))!
            .GetIndexes()
            .Single(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [
                        nameof(WarehouseTaskActionReceipt.OrganizationId),
                        nameof(WarehouseTaskActionReceipt.EnvironmentId),
                        nameof(WarehouseTaskActionReceipt.WarehouseTaskId),
                        nameof(WarehouseTaskActionReceipt.Action),
                        nameof(WarehouseTaskActionReceipt.IdempotencyKey),
                    ]))
            .GetDatabaseName()!;
        return new DbUpdateException(
            "unique conflict",
            new FakePostgresException("23505", constraintName));
    }

    private static ApplicationDbContext CreateConcurrentContext(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FakePostgresException(string sqlState, string constraintName) : Exception
    {
        public string SqlState { get; } = sqlState;

        public string ConstraintName { get; } = constraintName;
    }

    private sealed record ExpectedContract(
        string EndpointTypeName,
        string Route,
        string PermissionCode,
        string OperationId);
}
