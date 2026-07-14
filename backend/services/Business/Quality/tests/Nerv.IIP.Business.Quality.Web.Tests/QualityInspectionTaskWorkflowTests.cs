using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.MeasuringDevices;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionTaskWorkflowTests
{
    [Fact]
    public async Task Wms_inbound_completed_creates_pending_receiving_task_for_matching_plan()
    {
        await using var dbContext = CreateDbContext(nameof(Wms_inbound_completed_creates_pending_receiving_task_for_matching_plan));
        var plan = ActivePlan("PLAN-RCV-1000", "receiving", "SKU-RM-1000");
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        var handler = CreateWmsHandler(dbContext);

        await handler.HandleAsync(WmsInboundCompleted("IN-001", "LINE-001", "SKU-RM-1000", "inspection-required"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var task = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal(InspectionTaskStatuses.Pending, task.Status);
        Assert.Equal(plan.Id, task.InspectionPlanId);
        Assert.Equal("receiving", task.SourceType);
        Assert.Equal("wms", task.SourceService);
        Assert.Equal("IN-001", task.SourceDocumentId);
        Assert.Equal("LINE-001", task.SourceDocumentLineId);
        Assert.Equal("SKU-RM-1000", task.SkuCode);
        Assert.Equal(10m, task.Quantity);
    }

    [Fact]
    public async Task Wms_inbound_completed_creates_task_for_unlisted_quality_status_that_wms_gates()
    {
        await using var dbContext = CreateDbContext(nameof(Wms_inbound_completed_creates_task_for_unlisted_quality_status_that_wms_gates));
        dbContext.InspectionPlans.Add(ActivePlan("PLAN-RCV-IQC", "receiving", "SKU-RM-1000"));
        await dbContext.SaveChangesAsync();
        var handler = CreateWmsHandler(dbContext);

        await handler.HandleAsync(WmsInboundCompleted("IN-IQC", "LINE-001", "SKU-RM-1000", "iqc"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var task = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal("IN-IQC", task.SourceDocumentId);
        Assert.Equal("LINE-001", task.SourceDocumentLineId);
        Assert.Equal("SKU-RM-1000", task.SkuCode);
    }

    [Fact]
    public async Task Wms_inbound_completed_deduplicates_duplicate_lines_before_save()
    {
        await using var dbContext = CreateDbContext(nameof(Wms_inbound_completed_deduplicates_duplicate_lines_before_save));
        dbContext.InspectionPlans.Add(ActivePlan("PLAN-RCV-1000", "receiving", "SKU-RM-1000"));
        await dbContext.SaveChangesAsync();
        var handler = CreateWmsHandler(dbContext);

        await handler.HandleAsync(WmsInboundCompletedWithDuplicateLines(), CancellationToken.None);

        var task = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal("IN-DUP", task.SourceDocumentId);
        Assert.Equal("DUP-LINE", task.SourceDocumentLineId);
    }

    [Fact]
    public async Task Wms_inbound_completed_skips_exempt_or_sampling_skipped_lines()
    {
        await using var dbContext = CreateDbContext(nameof(Wms_inbound_completed_skips_exempt_or_sampling_skipped_lines));
        dbContext.InspectionPlans.Add(ActivePlan("PLAN-RCV-1000", "receiving", "SKU-RM-1000"));
        await dbContext.SaveChangesAsync();
        var handler = CreateWmsHandler(dbContext);

        await handler.HandleAsync(WmsInboundCompleted("IN-001", "LINE-001", "SKU-RM-1000", "inspection-exempt"), CancellationToken.None);
        await handler.HandleAsync(WmsInboundCompleted("IN-002", "LINE-001", "SKU-RM-1000", "sampling-skip"), CancellationToken.None);
        await handler.HandleAsync(WmsInboundCompleted("IN-003", "LINE-001", "SKU-RM-1000", "unrestricted"), CancellationToken.None);
        await handler.HandleAsync(WmsInboundCompleted("IN-004", "LINE-001", "SKU-RM-1000", "qualified"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Empty(await dbContext.InspectionTasks.ToListAsync());
    }

    [Fact]
    public async Task Create_record_from_task_prefills_source_context_and_completes_task()
    {
        await using var dbContext = CreateDbContext(nameof(Create_record_from_task_prefills_source_context_and_completes_task));
        var plan = ActivePlan("PLAN-RCV-1000", "receiving", "SKU-RM-1000");
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            "receiving",
            "wms",
            "IN-001",
            "LINE-001",
            "SKU-RM-1000",
            10m,
            "kg",
            "LOT-001",
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-001:LINE-001");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var handler = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator());

        var result = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(
                task.Id,
                "qa-user-001",
                [
                    new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])
                ],
                null,
                []),
            CancellationToken.None);
        var recordId = result.InspectionRecordId;
        await dbContext.SaveChangesAsync();

        // 合格：权威结论 passed，不开 NCR。
        Assert.Equal(InspectionRecordResults.Passed, result.Result);
        Assert.Null(result.NonconformanceReportId);
        var record = await dbContext.InspectionRecords.SingleAsync(x => x.Id == recordId);
        Assert.Equal("receiving", record.SourceType);
        Assert.Equal("wms", record.SourceService);
        Assert.Equal("IN-001", record.SourceDocumentId);
        Assert.Equal("SKU-RM-1000", record.SkuCode);
        var completedTask = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal(InspectionTaskStatuses.Completed, completedTask.Status);
        Assert.Equal(recordId, completedTask.InspectionRecordId);
    }

    [Fact]
    public async Task Create_record_from_task_opens_and_links_ncr_when_result_is_not_passed()
    {
        await using var dbContext = CreateDbContext(nameof(Create_record_from_task_opens_and_links_ncr_when_result_is_not_passed));
        var plan = ActivePlan("PLAN-RCV-2000", "receiving", "SKU-RM-2000");
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            "receiving",
            "wms",
            "IN-900",
            "LINE-001",
            "SKU-RM-2000",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-900:LINE-001");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var handler = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator());

        var result = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(
                task.Id,
                "qa-user-001",
                [
                    new InspectionResultLineCommandInput("appearance", "scratch", null, InspectionLineResults.Failed, "SCRATCH", 2m, [])
                ],
                "外观不良，判退",
                []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        // 权威结论 rejected → 后端同事务内自动开出 NCR 并回链到记录，返回业务编号供结果页互查。
        Assert.Equal(InspectionRecordResults.Rejected, result.Result);
        Assert.NotNull(result.NonconformanceReportId);
        var record = await dbContext.InspectionRecords.SingleAsync(x => x.Id == result.InspectionRecordId);
        Assert.Equal(result.NonconformanceReportId, record.NonconformanceReportId);
        var ncr = await dbContext.NonconformanceReports.SingleAsync();
        Assert.Equal(record.NonconformanceReportId, ncr.Id.ToString());
        Assert.Equal(ncr.NcrCode, result.NonconformanceReportCode);
        var completedTask = await dbContext.InspectionTasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal(InspectionTaskStatuses.Completed, completedTask.Status);
    }

    [Fact]
    public async Task Create_record_from_task_backfills_ncr_for_existing_rejected_record_without_ncr()
    {
        // 回归：既有 rejected 检验记录经常规检验流程建出、未开 NCR；随后任务命中 existing 分支
        // 完成时应补开并回链 NCR（否则端点会永久返回 NonconformanceReportId=null）。
        await using var dbContext = CreateDbContext(nameof(Create_record_from_task_backfills_ncr_for_existing_rejected_record_without_ncr));
        var plan = ActivePlan("PLAN-RCV-2100", "receiving", "SKU-RM-2100");
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        // 常规检验流程先建出一条 rejected 记录（此路径不开 NCR），此时尚无匹配任务。
        var regularRecordId = await new CreateInspectionRecordCommandHandler(
                new InspectionRecordRepository(dbContext),
                new InspectionPlanRepository(dbContext),
                new InspectionTaskRepository(dbContext))
            .Handle(
                new CreateInspectionRecordCommand(
                    "org-001",
                    "env-dev",
                    plan.Id,
                    "receiving",
                    "wms",
                    "IN-950",
                    "SKU-RM-2100",
                    10m,
                    null,
                    null,
                    [new InspectionResultLineCommandInput("appearance", "scratch", null, InspectionLineResults.Failed, "SCRATCH", 2m, [])],
                    "外观不良，判退",
                    []),
                CancellationToken.None);
        await dbContext.SaveChangesAsync();
        Assert.Empty(await dbContext.NonconformanceReports.ToArrayAsync());

        // 事后到达同来源单的待检任务，从任务提交命中既有记录。
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            "receiving",
            "wms",
            "IN-950",
            "LINE-001",
            "SKU-RM-2100",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-950:LINE-001");
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var handler = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator());
        var result = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(task.Id, "qa-user-001", [], null, []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(regularRecordId, result.InspectionRecordId);
        Assert.Equal(InspectionRecordResults.Rejected, result.Result);
        Assert.NotNull(result.NonconformanceReportId);
        var ncr = await dbContext.NonconformanceReports.SingleAsync();
        Assert.Equal(ncr.Id.ToString(), result.NonconformanceReportId);
        Assert.Equal(ncr.NcrCode, result.NonconformanceReportCode);
        var record = await dbContext.InspectionRecords.SingleAsync(x => x.Id == regularRecordId);
        Assert.Equal(ncr.Id.ToString(), record.NonconformanceReportId);
    }

    [Fact]
    public async Task Create_record_from_task_backfills_ncr_on_completed_replay_without_ncr()
    {
        // 回归：任务已完成（记录 rejected 但无 NCR）时的幂等重放应补开并回链 NCR，且不重复开单。
        await using var dbContext = CreateDbContext(nameof(Create_record_from_task_backfills_ncr_on_completed_replay_without_ncr));
        var plan = ActivePlan("PLAN-RCV-2200", "receiving", "SKU-RM-2200");
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            "receiving",
            "wms",
            "IN-960",
            "LINE-001",
            "SKU-RM-2200",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-960:LINE-001");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();

        // 常规检验流程建 rejected 记录并顺带完成匹配任务（此路径不开 NCR）。
        var recordId = await new CreateInspectionRecordCommandHandler(
                new InspectionRecordRepository(dbContext),
                new InspectionPlanRepository(dbContext),
                new InspectionTaskRepository(dbContext))
            .Handle(
                new CreateInspectionRecordCommand(
                    "org-001",
                    "env-dev",
                    plan.Id,
                    "receiving",
                    "wms",
                    "IN-960",
                    "SKU-RM-2200",
                    10m,
                    null,
                    null,
                    [new InspectionResultLineCommandInput("appearance", "scratch", null, InspectionLineResults.Failed, "SCRATCH", 2m, [])],
                    "外观不良，判退",
                    []),
                CancellationToken.None);
        await dbContext.SaveChangesAsync();
        Assert.Equal(InspectionTaskStatuses.Completed, (await dbContext.InspectionTasks.SingleAsync(x => x.Id == task.Id)).Status);
        Assert.Empty(await dbContext.NonconformanceReports.ToArrayAsync());

        var handler = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator());
        var result = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(task.Id, "qa-user-001", [], null, []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(recordId, result.InspectionRecordId);
        Assert.Equal(InspectionRecordResults.Rejected, result.Result);
        Assert.NotNull(result.NonconformanceReportId);
        var ncr = await dbContext.NonconformanceReports.SingleAsync();
        Assert.Equal(ncr.NcrCode, result.NonconformanceReportCode);

        // 再次重放：读同一 NCR，不重复开单（幂等）。
        var replay = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(task.Id, "qa-user-001", [], null, []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        Assert.Equal(result.NonconformanceReportId, replay.NonconformanceReportId);
        Assert.Equal(ncr.NcrCode, replay.NonconformanceReportCode);
        Assert.Single(await dbContext.NonconformanceReports.ToArrayAsync());
    }

    [Fact]
    public async Task Quality_seed_populates_reason_catalog_idempotently()
    {
        // 全新环境原因码目录为空会让检验执行的原因码 Picker 无码可选（MAN-457 真机走查发现）。
        // seed 幂等：重复执行不重复插入；已有码更新名称/分组等而非报错。
        await using var dbContext = CreateDbContext(nameof(Quality_seed_populates_reason_catalog_idempotently));
        var seed = new QualitySeedService(dbContext);

        await seed.SeedAsync("org-001", "env-dev");
        var first = await dbContext.QualityReasons.CountAsync();
        Assert.True(first >= 5);
        Assert.Contains(await dbContext.QualityReasons.ToArrayAsync(), x => x.ReasonCode == "RSN-APPEARANCE" && x.Enabled);

        await seed.SeedAsync("org-001", "env-dev");
        Assert.Equal(first, await dbContext.QualityReasons.CountAsync());
    }

    [Fact]
    public async Task Get_inspection_record_scopes_to_tenant_and_returns_ncr_backlink()
    {
        // PDA NCR 详情「来源检验记录」互链读：按 org/env 过滤（越权 id 与不存在同为 not found），
        // 返回回链的 NonconformanceReportId 供记录 → NCR 双向导航。
        await using var dbContext = CreateDbContext(nameof(Get_inspection_record_scopes_to_tenant_and_returns_ncr_backlink));
        var plan = ActivePlan("PLAN-RCV-2300", "receiving", "SKU-RM-2300");
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            "receiving",
            "wms",
            "IN-970",
            "LINE-001",
            "SKU-RM-2300",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-970:LINE-001");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var fromTask = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator());
        var created = await fromTask.Handle(
            new CreateInspectionRecordFromTaskCommand(
                task.Id,
                "qa-user-001",
                [
                    new InspectionResultLineCommandInput("appearance", "scratch", null, InspectionLineResults.Failed, "SCRATCH", 2m, [])
                ],
                "外观不良，判退",
                []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var handler = new GetInspectionRecordQueryHandler(dbContext);

        // 同租户：取到详情 + NCR 回链 + 结果行。
        var detail = await handler.Handle(
            new GetInspectionRecordQuery(created.InspectionRecordId, "org-001", "env-dev"),
            CancellationToken.None);
        Assert.Equal(InspectionRecordResults.Rejected, detail.Result);
        Assert.Equal(created.NonconformanceReportId, detail.NonconformanceReportId);
        Assert.Single(detail.ResultLines);

        // 越权租户：与不存在同为 not found，不泄露跨租户数据。
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new GetInspectionRecordQuery(created.InspectionRecordId, "org-other", "env-dev"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_regular_record_completes_matching_open_inspection_task()
    {
        await using var dbContext = CreateDbContext(nameof(Create_regular_record_completes_matching_open_inspection_task));
        var plan = ActivePlan("PLAN-RCV-1000", "receiving", "SKU-RM-1000");
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            "receiving",
            "wms",
            "IN-001",
            "LINE-001",
            "SKU-RM-1000",
            10m,
            "kg",
            "LOT-001",
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-001:LINE-001");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.Add(task);
        await dbContext.SaveChangesAsync();
        var handler = new CreateInspectionRecordCommandHandler(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new InspectionTaskRepository(dbContext));

        var recordId = await handler.Handle(
            new CreateInspectionRecordCommand(
                "org-001",
                "env-dev",
                plan.Id,
                "receiving",
                "wms",
                "IN-001",
                "SKU-RM-1000",
                10m,
                "LOT-001",
                null,
                [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
                null,
                []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var completedTask = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal(InspectionTaskStatuses.Completed, completedTask.Status);
        Assert.Equal(recordId, completedTask.InspectionRecordId);
    }

    [Fact]
    public async Task List_workbench_returns_pending_tasks_before_completed_tasks()
    {
        await using var dbContext = CreateDbContext(nameof(List_workbench_returns_pending_tasks_before_completed_tasks));
        var pending = NewTask("IN-001", "LINE-001", "SKU-RM-1000", DateTimeOffset.Parse("2026-07-06T08:00:00Z"));
        var completed = NewTask("IN-002", "LINE-001", "SKU-RM-2000", DateTimeOffset.Parse("2026-07-05T08:00:00Z"));
        completed.Start("qa-user-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        completed.Complete(new InspectionRecordId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b201")), DateTimeOffset.Parse("2026-07-05T10:00:00Z"));
        dbContext.InspectionTasks.AddRange(completed, pending);
        await dbContext.SaveChangesAsync();

        var result = await new ListInspectionTasksQueryHandler(dbContext).Handle(
            new ListInspectionTasksQuery("org-001", "env-dev", InspectionTaskStatuses.Pending, null, 0, 10),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(pending.Id, row.InspectionTaskId);
        Assert.Equal("IN-001", row.SourceDocumentId);
        Assert.Equal("SKU-RM-1000", row.SkuCode);
    }

    [Fact]
    public async Task Erp_purchase_receipt_recorded_creates_receiving_tasks_for_receipt_lines()
    {
        await using var dbContext = CreateDbContext(nameof(Erp_purchase_receipt_recorded_creates_receiving_tasks_for_receipt_lines));
        dbContext.InspectionPlans.Add(ActivePlan("PLAN-ERP-RCV-1000", "receiving", "SKU-RM-1000"));
        await dbContext.SaveChangesAsync();
        var handler = CreateErpHandler(dbContext);

        await handler.HandleAsync(ErpPurchaseReceiptRecorded(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var task = await dbContext.InspectionTasks.SingleAsync();
        Assert.Equal("erp", task.SourceService);
        Assert.Equal("PR-001", task.SourceDocumentId);
        Assert.Equal("PO-001-L1", task.SourceDocumentLineId);
        Assert.Equal("SKU-RM-1000", task.SkuCode);
    }

    [Fact]
    public async Task Mes_operation_and_finished_goods_events_create_operation_and_final_tasks()
    {
        await using var dbContext = CreateDbContext(nameof(Mes_operation_and_finished_goods_events_create_operation_and_final_tasks));
        dbContext.InspectionPlans.Add(ActivePlan("PLAN-OP-1000", "operation", "SKU-FG-1000", workCenterId: "WC-MIX"));
        dbContext.InspectionPlans.Add(ActivePlan("PLAN-FINAL-1000", "final", "SKU-FG-1000"));
        await dbContext.SaveChangesAsync();
        var operationHandler = CreateMesOperationHandler(dbContext);
        var finalHandler = CreateMesFinishedGoodsHandler(dbContext);

        await operationHandler.HandleAsync(MesOperationCompleted(requiresQualityInspection: true), CancellationToken.None);
        await finalHandler.HandleAsync(MesFinishedGoodsReceiptRequested(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Contains(await dbContext.InspectionTasks.ToArrayAsync(), x => x.SourceType == "operation" && x.SourceDocumentLineId == "OP-10");
        Assert.Contains(await dbContext.InspectionTasks.ToArrayAsync(), x => x.SourceType == "final" && x.SourceDocumentId == "FGR-001");
    }

    [Fact]
    public async Task Overdue_check_publishes_notification_event_for_pending_overdue_task_once()
    {
        await using var dbContext = CreateDbContext(nameof(Overdue_check_publishes_notification_event_for_pending_overdue_task_once));
        dbContext.InspectionTasks.Add(NewTask("IN-001", "LINE-001", "SKU-RM-1000", DateTimeOffset.Parse("2026-07-05T08:00:00Z")));
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingIntegrationEventPublisher();

        await new PublishOverdueInspectionTaskRemindersCommandHandler(dbContext, publisher).Handle(
            new PublishOverdueInspectionTaskRemindersCommand("org-001", "env-dev", DateTimeOffset.Parse("2026-07-05T09:00:00Z")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await new PublishOverdueInspectionTaskRemindersCommandHandler(dbContext, publisher).Handle(
            new PublishOverdueInspectionTaskRemindersCommand("org-001", "env-dev", DateTimeOffset.Parse("2026-07-05T10:00:00Z")),
            CancellationToken.None);

        var integrationEvent = Assert.IsType<InspectionTaskOverdueIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(QualityIntegrationEventTypes.InspectionTaskOverdue, integrationEvent.EventType);
        Assert.Equal("SKU-RM-1000", integrationEvent.Payload.SkuCode);
    }

    [Fact]
    public async Task Calibration_check_publishes_overdue_device_event_and_moves_device_to_calibration()
    {
        await using var dbContext = CreateDbContext(nameof(Calibration_check_publishes_overdue_device_event_and_moves_device_to_calibration));
        dbContext.MeasuringDevices.Add(MeasuringDevice.Create("org-001", "env-dev", "MD-001", "Micrometer", "0.001mm", 30, DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingIntegrationEventPublisher();

        await new PublishMeasuringDeviceCalibrationAlertsCommandHandler(dbContext, publisher).Handle(
            new PublishMeasuringDeviceCalibrationAlertsCommand("org-001", "env-dev", DateTimeOffset.Parse("2026-02-01T00:00:00Z")),
            CancellationToken.None);

        var integrationEvent = Assert.IsType<MeasuringDeviceCalibrationDueIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal("overdue", integrationEvent.Payload.CalibrationState);
        Assert.Equal("calibration", Assert.Single(dbContext.MeasuringDevices).Status);
    }

    [Fact]
    public async Task Calibration_check_does_not_republish_for_device_already_in_calibration()
    {
        await using var dbContext = CreateDbContext(nameof(Calibration_check_does_not_republish_for_device_already_in_calibration));
        var device = MeasuringDevice.Create("org-001", "env-dev", "MD-002", "Micrometer", "0.001mm", 30, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        device.MoveToCalibrationIfOverdue(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        dbContext.MeasuringDevices.Add(device);
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingIntegrationEventPublisher();

        var published = await new PublishMeasuringDeviceCalibrationAlertsCommandHandler(dbContext, publisher).Handle(
            new PublishMeasuringDeviceCalibrationAlertsCommand("org-001", "env-dev", DateTimeOffset.Parse("2026-02-02T00:00:00Z")),
            CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Empty(publisher.Published);
    }

    private static WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks CreateWmsHandler(ApplicationDbContext dbContext)
    {
        return new WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
    }

    private static ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks CreateErpHandler(ApplicationDbContext dbContext)
    {
        return new ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
    }

    private static MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks CreateMesOperationHandler(ApplicationDbContext dbContext)
    {
        return new MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
    }

    private static MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks CreateMesFinishedGoodsHandler(ApplicationDbContext dbContext)
    {
        return new MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
    }

    private static InspectionPlan ActivePlan(
        string planCode,
        string category,
        string skuCode,
        string? workCenterId = null)
    {
        var plan = InspectionPlan.Create("org-001", "env-dev", planCode, category, skuCode, null, workCenterId, null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionTask NewTask(string sourceDocumentId, string sourceDocumentLineId, string skuCode, DateTimeOffset dueAtUtc)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            new InspectionPlanId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b101")),
            "receiving",
            "wms",
            sourceDocumentId,
            sourceDocumentLineId,
            skuCode,
            10m,
            "kg",
            null,
            null,
            dueAtUtc.AddHours(-1),
            dueAtUtc,
            $"wms:inbound-completed:org-001:env-dev:{sourceDocumentId}:{sourceDocumentLineId}");
    }

    private static WmsIntegrationEvent WmsInboundCompleted(string inboundNo, string lineNo, string skuCode, string qualityStatus)
    {
        return new WmsIntegrationEvent(
            "evt-wms-001",
            WmsIntegrationEventTypes.InboundOrderCompleted,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            WmsIntegrationEventSources.BusinessWms,
            $"wms:inbound-completed:org-001:env-dev:{inboundNo}",
            inboundNo,
            "org-001",
            "env-dev",
            "system:wms",
            $"wms:inbound-completed:org-001:env-dev:{inboundNo}",
            new WmsIntegrationPayload(
                inboundNo,
                lineNo,
                skuCode,
                "kg",
                "SITE-01",
                "STAGE-01",
                10m,
                "Completed",
                null,
                null,
                [new WmsIntegrationPayloadLine(lineNo, skuCode, "kg", "SITE-01", "STAGE-01", 10m, qualityStatus)],
                "purchase-receipt",
                "PR-001"));
    }

    private static PurchaseReceiptRecordedIntegrationEvent ErpPurchaseReceiptRecorded()
    {
        return new PurchaseReceiptRecordedIntegrationEvent(
            "evt-erp-001",
            ErpIntegrationEventTypes.PurchaseReceiptRecorded,
            ErpIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            ErpIntegrationEventSources.BusinessErp,
            "corr-erp-001",
            "PR-001",
            "org-001",
            "env-dev",
            "system:erp",
            "erp:purchase-receipt-recorded:org-001:env-dev:PR-001",
            new PurchaseReceiptRecordedPayload(
                "PR-ID-001",
                "PR-001",
                "PO-001",
                "SUP-001",
                "SITE-01",
                "inspection-required",
                [
                    new PurchaseReceiptRecordedLinePayload("PO-001-L1", "SKU-RM-1000", "kg", "RCV-01", "LOT-001", 10m, "inspection-required")
                ]));
    }

    private static MesOperationTaskCompletedIntegrationEvent MesOperationCompleted(bool requiresQualityInspection)
    {
        return new MesOperationTaskCompletedIntegrationEvent(
            "evt-mes-op-001",
            MesIntegrationEventTypes.OperationTaskCompleted,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            MesIntegrationEventSources.BusinessMes,
            "corr-mes-001",
            "WO-001",
            "org-001",
            "env-dev",
            "system:mes",
            "mes:operation-completed:org-001:env-dev:WO-001:OP-10",
            new OperationTaskCompletedPayload("WO-001", "OP-10", "SKU-FG-1000", 10, "WC-MIX", 5m, "pcs", requiresQualityInspection, DateTimeOffset.Parse("2026-07-05T08:00:00Z")));
    }

    private static WmsIntegrationEvent WmsInboundCompletedWithDuplicateLines()
    {
        return new WmsIntegrationEvent(
            "evt-wms-dup",
            WmsIntegrationEventTypes.InboundOrderCompleted,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            WmsIntegrationEventSources.BusinessWms,
            "wms:inbound-completed:org-001:env-dev:IN-DUP",
            "IN-DUP",
            "org-001",
            "env-dev",
            "system:wms",
            "wms:inbound-completed:org-001:env-dev:IN-DUP",
            new WmsIntegrationPayload(
                "IN-DUP",
                null,
                null,
                null,
                "SITE-01",
                "STAGE-01",
                null,
                "Completed",
                null,
                null,
                [
                    new WmsIntegrationPayloadLine("DUP-LINE", "SKU-RM-1000", "kg", "SITE-01", "STAGE-01", 10m, "inspection-required"),
                    new WmsIntegrationPayloadLine("DUP-LINE", "SKU-RM-1000", "kg", "SITE-01", "STAGE-01", 10m, "inspection-required")
                ],
                "purchase-receipt",
                "PR-DUP"));
    }

    private static FinishedGoodsReceiptRequestedIntegrationEvent MesFinishedGoodsReceiptRequested()
    {
        return new FinishedGoodsReceiptRequestedIntegrationEvent(
            "evt-mes-fgr-001",
            MesIntegrationEventTypes.FinishedGoodsReceiptRequested,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"),
            MesIntegrationEventSources.BusinessMes,
            "corr-mes-fgr-001",
            "FGR-001",
            "org-001",
            "env-dev",
            "system:mes",
            "mes:finished-goods-receipt-requested:org-001:env-dev:FGR-001",
            new FinishedGoodsReceiptRequestedPayload("FGR-001", "WO-001", "SKU-FG-1000", 5m, "pcs", "LOT-FG-001", null, DateTimeOffset.Parse("2026-07-05T08:30:00Z")));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(integrationEvent!);
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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
