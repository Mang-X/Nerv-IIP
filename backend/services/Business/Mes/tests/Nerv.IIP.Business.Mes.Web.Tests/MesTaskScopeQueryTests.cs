using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesTaskScopeQueryTests
{
    [Fact]
    public async Task Operation_scope_filters_intersect_and_keep_total_consistent_with_the_page()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        SeedTask(dbContext, "WO-01", "OP-01", "WC-01", "emp010", "TEAM-A", now);
        SeedTask(dbContext, "WO-02", "OP-02", "WC-01", "emp011", "TEAM-A", now);
        SeedTask(dbContext, "WO-03", "OP-03", "WC-02", "emp010", "TEAM-B", now);
        await dbContext.SaveChangesAsync();

        var result = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Skip: 0,
                Take: 1,
                AssignedUserIds: "emp010,emp099",
                TeamIds: "TEAM-A",
                WorkCenterIds: "WC-01"),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("OP-01", Assert.Single(result.Items).OperationTaskId);

        var self = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 10,
                AssignedUserIds: "emp010"),
            CancellationToken.None);
        var team = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 10,
                TeamIds: "TEAM-A"),
            CancellationToken.None);
        var workCenter = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 10,
                WorkCenterIds: "WC-02"),
            CancellationToken.None);
        var emptyCanonicalScope = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 10,
                AssignedUserIds: "  "),
            CancellationToken.None);

        Assert.Equal(["OP-01", "OP-03"], self.Items.Select(x => x.OperationTaskId).Order().ToArray());
        Assert.Equal(["OP-01", "OP-02"], team.Items.Select(x => x.OperationTaskId).Order().ToArray());
        Assert.Equal("OP-03", Assert.Single(workCenter.Items).OperationTaskId);
        Assert.Equal(0, emptyCanonicalScope.Total);
        Assert.Empty(emptyCanonicalScope.Items);
    }

    [Fact]
    public async Task Exact_strong_ids_are_filtered_with_scope_before_paging()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        for (var index = 0; index < 500; index++)
        {
            SeedTask(
                dbContext,
                $"WO-A{index:000}-WO-Z-SCOPE",
                $"OP-A{index:000}-OP-Z-SCOPE",
                "WC-01",
                "emp010",
                "TEAM-A",
                now.AddSeconds(index));
        }

        SeedTask(
            dbContext,
            "WO-Z-SCOPE",
            "OP-Z-SCOPE",
            "WC-01",
            "emp010",
            "TEAM-A",
            now.AddMinutes(10));
        await dbContext.SaveChangesAsync();

        var workOrders = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery(
                "org-001",
                "env-dev",
                null,
                Take: 1,
                AssignedUserIds: "emp010",
                WorkOrderId: "WO-Z-SCOPE"),
            CancellationToken.None);
        var operationTasks = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 1,
                AssignedUserIds: "emp010",
                WorkOrderId: "WO-Z-SCOPE",
                OperationTaskId: "OP-Z-SCOPE"),
            CancellationToken.None);

        Assert.Equal(1, workOrders.Total);
        Assert.Equal("WO-Z-SCOPE", Assert.Single(workOrders.Items).WorkOrderId);
        Assert.Equal(1, operationTasks.Total);
        Assert.Equal("OP-Z-SCOPE", Assert.Single(operationTasks.Items).OperationTaskId);
    }

    [Fact]
    public async Task Work_order_scope_uses_operation_exists_and_intersects_the_business_work_center_filter()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        SeedTask(dbContext, "WO-01", "OP-01", "WC-01", "emp010", "TEAM-A", now);
        SeedTask(dbContext, "WO-01", "OP-01-OTHER", "WC-01", "emp011", "TEAM-A", now.AddSeconds(1));
        SeedTask(dbContext, "WO-02", "OP-02", "WC-02", "emp010", "TEAM-A", now.AddMinutes(1));
        SeedTask(dbContext, "WO-03", "OP-03", "WC-01", "emp011", "TEAM-A", now.AddMinutes(2));
        await dbContext.SaveChangesAsync();

        var result = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery(
                "org-001",
                "env-dev",
                null,
                WorkCenterId: "WC-01",
                AssignedUserIds: "emp010",
                TeamIds: "TEAM-A",
                WorkCenterIds: "WC-01,WC-02"),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("WO-01", item.WorkOrderId);
        var operation = Assert.Single(item.OperationTasks);
        Assert.NotEqual(default, operation.EvaluatedAtUtc);
        Assert.Contains(operation.BlockReasons, x =>
            x.Contains("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reportable_query_returns_only_in_progress_owned_tasks_with_stable_total_and_page()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        var first = SeedTask(dbContext, "WO-01", "OP-01", "WC-01", "emp010", "TEAM-A", now);
        first.Start(now.AddMinutes(1));
        var second = SeedTask(dbContext, "WO-02", "OP-02", "WC-01", "emp010", "TEAM-A", now.AddMinutes(1));
        second.Start(now.AddMinutes(2));
        SeedTask(dbContext, "WO-03", "OP-03", "WC-01", "emp010", "TEAM-A", now.AddMinutes(2));
        SeedTask(dbContext, "WO-04", "OP-04", "WC-01", "emp011", "TEAM-A", now.AddMinutes(3)).Start(now.AddMinutes(4));
        await dbContext.SaveChangesAsync();

        var result = await new ListReportableOperationTasksQueryHandler(dbContext).Handle(
            new ListReportableOperationTasksQuery(
                "org-001",
                "env-dev",
                Status: nameof(OperationTaskLifecycleStatus.InProgress),
                Skip: 1,
                Take: 1,
                AssignedUserIds: "emp010"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("OP-02", item.OperationTaskId);
        Assert.Contains("report", item.AllowedActions);

        var incompatibleStatus = await new ListReportableOperationTasksQueryHandler(dbContext).Handle(
            new ListReportableOperationTasksQuery(
                "org-001",
                "env-dev",
                Status: nameof(OperationTaskLifecycleStatus.Completed),
                AssignedUserIds: "emp010"),
            CancellationToken.None);

        Assert.Equal(0, incompatibleStatus.Total);
        Assert.Empty(incompatibleStatus.Items);
    }

    [Fact]
    public async Task Queued_task_without_material_snapshot_is_blocked_while_lifecycle_actions_are_authoritative()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        var queued = SeedTask(dbContext, "WO-QUEUED", "OP-QUEUED", "WC-01", "emp012", "TEAM-AS", now);
        var inProgress = SeedTask(dbContext, "WO-ACTIVE", "OP-ACTIVE", "WC-01", "emp010", "TEAM-MC", now.AddMinutes(1));
        inProgress.Start(now.AddMinutes(2));
        var paused = SeedTask(dbContext, "WO-PAUSED", "OP-PAUSED", "WC-01", "emp010", "TEAM-MC", now.AddMinutes(2));
        paused.Start(now.AddMinutes(3));
        paused.Pause(now.AddMinutes(4));
        var completed = SeedTask(dbContext, "WO-DONE", "OP-DONE", "WC-01", "emp010", "TEAM-MC", now.AddMinutes(3));
        completed.Start(now.AddMinutes(4));
        completed.Complete(now.AddMinutes(5));
        await dbContext.SaveChangesAsync();

        var result = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", null, Take: 10),
            CancellationToken.None);

        var queuedRow = Assert.Single(result.Items, x => x.OperationTaskId == queued.OperationTaskIdValue);
        Assert.Empty(queuedRow.AllowedActions);
        Assert.Contains(queuedRow.BlockReasons, x => x.Contains("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", StringComparison.Ordinal));
        Assert.Equal(["pause", "complete", "report"], Assert.Single(result.Items, x => x.OperationTaskId == inProgress.OperationTaskIdValue).AllowedActions);
        Assert.Equal(["resume"], Assert.Single(result.Items, x => x.OperationTaskId == paused.OperationTaskIdValue).AllowedActions);
        Assert.Empty(Assert.Single(result.Items, x => x.OperationTaskId == completed.OperationTaskIdValue).AllowedActions);
        Assert.All(result.Items, x => Assert.NotEqual(default, x.EvaluatedAtUtc));

        var unknown = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", "not-a-status", Take: 10),
            CancellationToken.None);
        Assert.Equal(0, unknown.Total);
        Assert.Empty(unknown.Items);
    }

    /// <summary>
    /// MAN-698 台账 #35：缺料阻塞原因此前在三处各写一套，其中齐套读面与下达前置检查直出英文生码
    /// 「物料编码 + shortage + 数量」，界面上既读不懂又被徽标截断。现在统一走
    /// <see cref="MaterialReadinessGuards.FormatShortageReason"/>：读面 <c>CODE: 中文</c>，
    /// 写操作被拒的文案剥掉码只留中文。
    /// </summary>
    [Fact]
    public void Material_shortage_reason_is_chinese_and_strips_the_code_for_user_facing_messages()
    {
        var withLot = MaterialReadinessGuards.FormatShortageReason("MAT-OIL", "LOT-OIL-A", 2.5m);
        var withoutLot = MaterialReadinessGuards.FormatShortageReason("MAT-BEARING", null, 7m);

        Assert.Equal("MATERIAL_SHORTAGE: 物料 MAT-OIL，批次 LOT-OIL-A 缺口 2.5", withLot);
        Assert.Equal("MATERIAL_SHORTAGE: 物料 MAT-BEARING 缺口 7", withoutLot);
        Assert.DoesNotContain("shortage ", withLot, StringComparison.OrdinalIgnoreCase);

        var userFacing = MaterialReadinessGuards.DescribeForUser(
            [withLot, withoutLot, MaterialReadinessGuards.MissingRequirementSnapshotReason]);

        Assert.DoesNotContain("MATERIAL_SHORTAGE", userFacing, StringComparison.Ordinal);
        Assert.DoesNotContain("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", userFacing, StringComparison.Ordinal);
        Assert.Contains("物料 MAT-OIL，批次 LOT-OIL-A 缺口 2.5", userFacing, StringComparison.Ordinal);
        Assert.Contains("工单缺少齐套需求快照", userFacing, StringComparison.Ordinal);
        // 中文说明里自带冒号的原因不能被当成码剥掉半句。
        Assert.Equal("物料齐套未满足：还差 3 件", MaterialReadinessGuards.DescribeForUser(["物料齐套未满足：还差 3 件"]));
    }

    [Fact]
    public async Task Emp012_blocked_task_returns_predecessor_and_material_shortage_reasons_together()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-ASSEMBLY",
            "SKU-ASSEMBLY",
            "PV-001",
            10m,
            1,
            now.AddDays(1),
            "PCS");
        workOrder.MarkReleased();
        var previous = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-ASSEMBLY",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-ASSEMBLY",
            [],
            now,
            TimeSpan.FromHours(1),
            null,
            null);
        var blocked = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-ASSEMBLY",
            "OP-20",
            OperationTaskLifecycleStatus.Queued,
            20,
            "WC-ASSEMBLY",
            [],
            now.AddMinutes(1),
            TimeSpan.FromHours(1),
            null,
            null);
        previous.Assign("emp011", null, null, now, teamId: "TEAM-AS");
        blocked.Assign("emp012", null, null, now, teamId: "TEAM-AS");
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(previous, blocked);
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-ASSEMBLY",
            "OP-20",
            "MAT-BEARING",
            null,
            10m,
            2m,
            1m,
            "ProductEngineering",
            "SNAP-ASSEMBLY",
            now));
        await dbContext.SaveChangesAsync();

        var result = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 10,
                AssignedUserIds: "emp012"),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Empty(row.AllowedActions);
        Assert.Contains(row.BlockReasons, x =>
            x.Contains("PREVIOUS_OPERATION_INCOMPLETE", StringComparison.Ordinal) &&
            x.Contains("前序工序尚未完成", StringComparison.Ordinal));
        Assert.Contains(row.BlockReasons, x =>
            x.Contains("MATERIAL_SHORTAGE", StringComparison.Ordinal) &&
            x.Contains("物料 MAT-BEARING", StringComparison.Ordinal) &&
            x.Contains("缺口 7", StringComparison.Ordinal));

        var commandException = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand(
                    "org-001",
                    "env-dev",
                    "OP-20",
                    "start",
                    now.AddMinutes(5)),
                CancellationToken.None));
        // 读面保留 `CODE: 中文`（前端按码取标签/下一步），但写操作被拒的文案会经分层透传直接上屏，
        // 因此必须已剥掉英文码、只剩中文人话（MAN-698 台账 #35）。
        Assert.Contains("前序工序尚未完成", commandException.Message, StringComparison.Ordinal);
        Assert.Contains("物料 MAT-BEARING", commandException.Message, StringComparison.Ordinal);
        Assert.Contains("缺口 7", commandException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PREVIOUS_OPERATION_INCOMPLETE", commandException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MATERIAL_SHORTAGE", commandException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shortage", commandException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Queued_task_with_complete_material_snapshot_and_no_other_blocker_allows_start()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        SeedTask(dbContext, "WO-READY", "OP-READY", "WC-01", "emp010", "TEAM-MC", now);
        var workOrder = Assert.Single(
            dbContext.WorkOrders.Local,
            x => x.WorkOrderIdValue == "WO-READY");
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotCapturedStatus,
            now);
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-READY",
            "OP-READY",
            "MAT-READY",
            null,
            10m,
            10m,
            0m,
            "ProductEngineering",
            "SNAP-READY",
            now));
        await dbContext.SaveChangesAsync();

        var result = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                nameof(OperationTaskLifecycleStatus.Queued),
                WorkOrderId: "WO-READY"),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(["start"], row.AllowedActions);
        Assert.Empty(row.BlockReasons);
    }

    [Fact]
    public async Task Legacy_material_rows_without_current_production_version_proof_fail_closed()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        var task = SeedTask(
            dbContext,
            "WO-LEGACY-MATERIAL",
            "OP-LEGACY-MATERIAL",
            "WC-01",
            "emp010",
            "TEAM-MC",
            now);
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            task.WorkOrderId,
            task.OperationTaskIdValue,
            "MAT-LEGACY",
            null,
            10m,
            10m,
            0m,
            "Legacy",
            "SNAP-LEGACY",
            now.AddDays(-1)));
        await dbContext.SaveChangesAsync();

        var list = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                nameof(OperationTaskLifecycleStatus.Queued),
                WorkOrderId: task.WorkOrderId),
            CancellationToken.None);

        var row = Assert.Single(list.Items);
        Assert.Empty(row.AllowedActions);
        Assert.Contains(
            MaterialReadinessGuards.MissingRequirementSnapshotReason,
            row.BlockReasons);

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand(
                    "org-001",
                    "env-dev",
                    task.OperationTaskIdValue,
                    "start",
                    now.AddMinutes(1)),
                CancellationToken.None));
        // 写操作被拒的文案已剥掉英文码，只留中文（读面仍保留 `CODE: 中文`）。
        Assert.Contains(
            "工单缺少齐套需求快照",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durable_no_requirement_snapshot_keeps_list_and_start_action_consistent()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        var task = SeedTask(dbContext, "WO-NO-MATERIAL", "OP-NO-MATERIAL", "WC-01", "emp010", "TEAM-MC", now);
        var workOrder = Assert.Single(
            dbContext.WorkOrders.Local,
            x => x.WorkOrderIdValue == "WO-NO-MATERIAL");

        var capture = await MaterialReadinessGuards.EnsureRequirementSnapshotsAsync(
            dbContext,
            NoRequirementsSnapshotProvider.Instance,
            workOrder,
            now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(capture.NoRequirements);
        Assert.Equal(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            workOrder.MaterialRequirementSnapshotStatus);

        var list = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                nameof(OperationTaskLifecycleStatus.Queued),
                AssignedUserIds: "emp010"),
            CancellationToken.None);
        var row = Assert.Single(list.Items);
        Assert.Equal(task.OperationTaskIdValue, row.OperationTaskId);
        Assert.Equal(["start"], row.AllowedActions);
        Assert.Empty(row.BlockReasons);

        var action = await new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand(
                    "org-001",
                    "env-dev",
                    task.OperationTaskIdValue,
                    "start",
                    now.AddMinutes(1)),
                CancellationToken.None);

        Assert.Equal(nameof(OperationTaskLifecycleStatus.InProgress), action.Status);
    }

    [Fact]
    public async Task Non_completing_report_rejects_a_terminal_operation_task()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = Utc("2026-07-29T08:00:00Z");
        var task = SeedTask(dbContext, "WO-DONE", "OP-DONE", "WC-01", "emp010", "TEAM-MC", now);
        task.Start(now.AddMinutes(1));
        task.Complete(now.AddMinutes(2));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new RecordProductionReportCommandHandler(dbContext).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-DONE",
                    "OP-DONE",
                    1m,
                    0m,
                    false,
                    now.AddMinutes(3)),
                CancellationToken.None));

        Assert.Equal("report", exception.Action);
        Assert.Equal(nameof(OperationTaskLifecycleStatus.Completed), exception.CurrentStatus);
    }

    [Fact]
    public void Reportable_operation_endpoint_contract_is_registered()
    {
        var contract = MesEndpointContracts.Get<ListReportableOperationTasksEndpoint>();

        Assert.Equal("GET", contract.HttpMethod);
        Assert.Equal("/api/business/v1/mes/reportable-operation-tasks", contract.Route);
        Assert.Equal(MesPermissionCodes.ReportingRead, contract.PermissionCode);
        Assert.Equal("listBusinessMesReportableOperationTasks", contract.OperationId);
    }

    private static OperationTask SeedTask(
        Infrastructure.ApplicationDbContext dbContext,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        string assignedUserId,
        string teamId,
        DateTimeOffset earliestStartUtc)
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            workOrderId,
            $"SKU-{workOrderId}",
            "PV-001",
            10m,
            1,
            earliestStartUtc.AddDays(1),
            "PCS");
        workOrder.MarkReleased();
        var task = OperationTask.Create(
            "org-001",
            "env-dev",
            workOrderId,
            operationTaskId,
            OperationTaskLifecycleStatus.Queued,
            10,
            workCenterId,
            [],
            earliestStartUtc,
            TimeSpan.FromHours(1),
            null,
            null);
        task.Assign(
            assignedUserId,
            null,
            null,
            earliestStartUtc,
            assignedUserName: assignedUserId,
            teamId: teamId,
            teamName: teamId);
        if (!dbContext.WorkOrders.Local.Any(x => x.WorkOrderIdValue == workOrderId))
        {
            dbContext.WorkOrders.Add(workOrder);
        }

        dbContext.OperationTasks.Add(task);
        return task;
    }

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value);

    private sealed class NoRequirementsSnapshotProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static NoRequirementsSnapshotProvider Instance { get; } = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}
