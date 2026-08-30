using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class NcrReworkRequestedHandlerPostgresTests
{
    [MesRealPostgresFact]
    public async Task Rework_request_creates_one_source_linked_work_order_and_persists_inbox_and_numbering()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        var integrationEvent = CreateEvent(
            requestedAtUtc: DateTimeOffset.Parse("2026-08-29T08:00:00Z").AddTicks(9));

        ReworkWorkOrderCreatedDomainEvent createdDomainEvent;
        await using (var creationScope = provider.CreateAsyncScope())
        {
            var creationDb = creationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coordinator = new CapturingReworkScopeCoordinator(
                creationScope.ServiceProvider.GetRequiredService<IMesReworkWorkOrderScopeCoordinator>(),
                creationDb);
            await CreateHandler(creationScope.ServiceProvider, coordinator)
                .HandleAsync(integrationEvent, CancellationToken.None);
            createdDomainEvent = Assert.IsType<ReworkWorkOrderCreatedDomainEvent>(coordinator.Captured);
        }
        await HandleAsync(provider, integrationEvent);

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = await db.WorkOrders.SingleAsync(x => x.SourceNcrId == "ncr-001");
        Assert.Equal(WorkOrder.ReworkType, workOrder.WorkOrderType);
        Assert.Equal("WO-SOURCE-001", workOrder.SourceWorkOrderId);
        Assert.Equal("OP-SOURCE-20", workOrder.SourceOperationTaskId);
        Assert.Equal("NCR-2026-0001", workOrder.SourceNcrCode);
        Assert.Equal("SKU-001", workOrder.SkuId);
        Assert.Equal(3m, workOrder.Quantity);
        Assert.Equal("LOT-001", workOrder.SourceLotNo);
        Assert.Equal("SN-001", workOrder.SourceSerialNo);
        Assert.Equal(DateTimeOffset.Parse("2026-08-29T08:00:00Z"), workOrder.SourceReworkRequestedAtUtc);
        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
        Assert.Equal(WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus, workOrder.MaterialRequirementSnapshotStatus);
        Assert.Equal("PV-001", workOrder.MaterialRequirementSnapshotProductionVersionId);
        var operationTasks = await db.OperationTasks
            .Where(x => x.WorkOrderId == workOrder.WorkOrderIdValue)
            .OrderBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .ToArrayAsync();
        Assert.Collection(
            operationTasks,
            first =>
            {
                Assert.Equal(20, first.OperationSequence);
                Assert.Equal("WC-020", first.WorkCenterId);
                Assert.Equal(["WC-020-B", "WC-020-C"], first.AlternativeWorkCenterIdList);
                Assert.Equal(TimeSpan.FromMinutes(20), first.Duration);
                Assert.True(first.RequiresQualityInspection);
                Assert.Equal("OP-CODE-020", first.OperationCode);
                Assert.Equal(OperationTaskLifecycleStatus.Queued, first.Status);
            },
            second =>
            {
                Assert.Equal(30, second.OperationSequence);
                Assert.Equal("WC-030", second.WorkCenterId);
                Assert.Empty(second.AlternativeWorkCenterIdList);
                Assert.Equal(TimeSpan.FromMinutes(30), second.Duration);
                Assert.False(second.RequiresQualityInspection);
                Assert.Equal("OP-CODE-030", second.OperationCode);
                Assert.Equal(OperationTaskLifecycleStatus.Queued, second.Status);
            });
        Assert.Equal(
            operationTasks.Length,
            operationTasks.Select(x => x.OperationTaskIdValue).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(operationTasks, x => x.OperationTaskIdValue is "OP-SOURCE-20" or "OP-SOURCE-30");

        var sourceWorkOrder = await db.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-SOURCE-001");
        sourceWorkOrder.RebindProductionVersionForEngineeringChange("PV-002");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var frozenReworkWorkOrder = await db.WorkOrders.SingleAsync(x => x.SourceNcrId == "ncr-001");
        var frozenFirstOperation = await db.OperationTasks
            .OrderBy(x => x.OperationSequence)
            .FirstAsync(x => x.WorkOrderId == frozenReworkWorkOrder.WorkOrderIdValue);
        Assert.Equal("PV-001", frozenReworkWorkOrder.ProductionVersionId);
        Assert.Equal("WC-020", frozenFirstOperation.WorkCenterId);
        Assert.Equal("OP-CODE-020", frozenFirstOperation.OperationCode);
        Assert.Equal(TimeSpan.FromMinutes(20), frozenFirstOperation.Duration);

        var queried = await new ListOperationTasksQueryHandler(db, new FixedTimeProvider(integrationEvent.Payload.RequestedAtUtc))
            .Handle(
                new ListOperationTasksQuery(
                    "org-001",
                    "env-dev",
                    null,
                    WorkOrderId: workOrder.WorkOrderIdValue),
                CancellationToken.None);
        Assert.Equal(operationTasks.Select(x => x.OperationTaskIdValue), queried.Items.Select(x => x.OperationTaskId));
        Assert.Equal(["start"], queried.Items.First().AllowedActions);
        Assert.All(queried.Items, AssertReworkAuthority);

        var listedWorkOrder = Assert.Single((await new ListMesWorkOrdersQueryHandler(db).Handle(
            new ListMesWorkOrdersQuery(
                "org-001",
                "env-dev",
                null,
                WorkOrderId: workOrder.WorkOrderIdValue),
            CancellationToken.None)).Items);
        AssertReworkAuthority(listedWorkOrder);

        var detailedWorkOrder = await new GetMesWorkOrderDetailQueryHandler(db).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", workOrder.WorkOrderIdValue),
            CancellationToken.None);
        AssertReworkAuthority(detailedWorkOrder);
        Assert.All(detailedWorkOrder.OperationTasks, AssertReworkAuthority);

        var firstTaskId = operationTasks[0].OperationTaskIdValue;
        var actionHandler = new ChangeOperationTaskStateCommandHandler(db);
        var startedAtUtc = integrationEvent.Payload.RequestedAtUtc.AddMinutes(1);
        await actionHandler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", firstTaskId, "start", startedAtUtc, "rework:start"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        await actionHandler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", firstTaskId, "pause", startedAtUtc.AddMinutes(1), "rework:pause"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        await actionHandler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", firstTaskId, "resume", startedAtUtc.AddMinutes(2), "rework:resume"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var reportableTask = Assert.Single((await new ListReportableOperationTasksQueryHandler(db).Handle(
            new ListReportableOperationTasksQuery(
                "org-001",
                "env-dev",
                WorkOrderId: workOrder.WorkOrderIdValue),
            CancellationToken.None)).Items);
        AssertReworkAuthority(reportableTask);
        var report = await new RecordProductionReportCommandHandler(
                db,
                TestProductionReportOeeDimensionSnapshotProvider.Instance,
                assertionScope.ServiceProvider.GetRequiredService<MesCodingService>())
            .Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    workOrder.WorkOrderIdValue,
                    firstTaskId,
                    1m,
                    0m,
                    false,
                    startedAtUtc.AddMinutes(3),
                    "rework:report"),
                CancellationToken.None);
        await db.SaveChangesAsync();
        await actionHandler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", firstTaskId, "complete", startedAtUtc.AddMinutes(4), "rework:complete"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.True(await db.ProductionReports.AnyAsync(x => x.ReportNo == report.ReportNo && x.OperationTaskId == firstTaskId));
        Assert.Equal(
            OperationTaskLifecycleStatus.Completed,
            (await db.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == firstTaskId)).Status);
        Assert.Single(await db.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
            .ToArrayAsync());
        Assert.Empty(await new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db)
            .ListAsync(
                NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None));
        var numbering = await db.CodeIdempotencyKeys.SingleAsync(x => x.IdempotencyKey == CreateEvent().IdempotencyKey);
        Assert.Equal(workOrder.WorkOrderIdValue, numbering.Code);
        var receipt = new ReworkWorkOrderCreatedIntegrationEventConverter().Convert(createdDomainEvent);
        Assert.Equal("corr-001", receipt.CorrelationId);
        Assert.Equal("evt-rework-001", receipt.CausationId);
        Assert.Equal("NCR-2026-0001", receipt.Payload.SourceNcrCode);
        Assert.Equal("SKU-001", receipt.Payload.SkuCode);
        Assert.Equal("LOT-001", receipt.Payload.SourceLotNo);
        Assert.Equal("SN-001", receipt.Payload.SourceSerialNo);

        db.WorkOrders.Add(WorkOrder.CreateRework(
            "org-001",
            "env-dev",
            "WO-RW-DUPLICATE",
            "SKU-001",
            "PV-001",
            "PCS",
            3m,
            100,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            "WO-SOURCE-001",
            "OP-SOURCE-20",
            "DEF-001",
            "ncr-001",
            "NCR-2026-0001",
            "LOT-001",
            "SN-001",
            DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
            "corr-duplicate",
            "evt-duplicate"));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var uniqueViolation = Assert.IsType<PostgresException>(duplicate.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, uniqueViolation.SqlState);
        Assert.Equal("ux_work_orders_scope_source_ncr", uniqueViolation.ConstraintName);
        db.ChangeTracker.Clear();

        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mes.work_orders
            SET work_order_type = 'rework'
            WHERE organization_id = 'org-001'
              AND environment_id = 'env-dev'
              AND work_order_id = 'WO-SOURCE-001'
            """;
        var invalidSource = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidSource.SqlState);
        Assert.Equal("ck_work_orders_rework_source", invalidSource.ConstraintName);
    }

    private static void AssertReworkAuthority(MesWorkOrderExecutionFact item) => AssertReworkAuthority(
        item.WorkOrderType,
        item.SourceWorkOrderId,
        item.SourceNcrId,
        item.SourceNcrCode);

    private static void AssertReworkAuthority(MesWorkOrderDetailResponse item) => AssertReworkAuthority(
        item.WorkOrderType,
        item.SourceWorkOrderId,
        item.SourceNcrId,
        item.SourceNcrCode);

    private static void AssertReworkAuthority(MesOperationTaskRow item) => AssertReworkAuthority(
        item.WorkOrderType,
        item.SourceWorkOrderId,
        item.SourceNcrId,
        item.SourceNcrCode);

    private static void AssertReworkAuthority(
        string workOrderType,
        string? sourceWorkOrderId,
        string? sourceNcrId,
        string? sourceNcrCode)
    {
        Assert.Equal(WorkOrder.ReworkType, workOrderType);
        Assert.Equal("WO-SOURCE-001", sourceWorkOrderId);
        Assert.Equal("ncr-001", sourceNcrId);
        Assert.Equal("NCR-2026-0001", sourceNcrCode);
    }

    [MesRealPostgresFact]
    public async Task Rework_release_captures_material_shortage_and_blocks_start()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using (var missingProvider = await CreateMigratedProviderAsync(
            new StaticMaterialSnapshotProvider(MesMaterialRequirementSnapshotResult.Missing("product-engineering:mbom:missing"))))
        {
            await SeedSourceAsync(missingProvider, "org-001", "env-dev");
            var exception = await Assert.ThrowsAsync<KnownException>(() =>
                HandleAsync(missingProvider, CreateEvent()));
            Assert.Equal(MaterialReadinessGuards.MissingRequirementSnapshotReason, exception.Message);

            await using var missingAssertionScope = missingProvider.CreateAsyncScope();
            var missingDb = missingAssertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await missingDb.WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());
            Assert.Empty(await missingDb.ProcessedIntegrationEvents
                .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
                .ToArrayAsync());
        }

        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var materialSnapshotProvider = new StaticMaterialSnapshotProvider(
            MesMaterialRequirementSnapshotResult.Captured(
                "product-engineering:PV-001",
                [new MesMaterialRequirementSnapshotLine(
                    null,
                    "MAT-REWORK",
                    null,
                    5m,
                    "PCS",
                    2m,
                    0m,
                    "PV-001:MAT-REWORK",
                    [])]));
        await using var provider = await CreateMigratedProviderAsync(materialSnapshotProvider);
        await SeedSourceAsync(provider, "org-001", "env-dev");

        await HandleAsync(provider, CreateEvent());

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = await db.WorkOrders.SingleAsync(x => x.SourceNcrId == "ncr-001");
        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
        Assert.Equal(WorkOrder.MaterialRequirementSnapshotCapturedStatus, workOrder.MaterialRequirementSnapshotStatus);
        var requirement = await db.MaterialRequirements.SingleAsync(x => x.WorkOrderId == workOrder.WorkOrderIdValue);
        Assert.Equal("MAT-REWORK", requirement.MaterialId);
        Assert.Equal(5m, requirement.RequiredQuantity);
        Assert.Equal(2m, requirement.AvailableQuantity);

        var queried = await new ListOperationTasksQueryHandler(db).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                WorkOrderId: workOrder.WorkOrderIdValue),
            CancellationToken.None);
        var firstTask = queried.Items.First();
        Assert.Empty(firstTask.AllowedActions);
        Assert.Contains("MATERIAL_SHORTAGE: 物料 MAT-REWORK 缺口 3", firstTask.BlockReasons);
    }

    [MesRealPostgresFact]
    public async Task Different_ncrs_in_same_scope_create_unique_operation_task_ids()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await SeedSourceAsync(
            provider,
            "org-001",
            "env-dev",
            sourceWorkOrderId: "WO-SOURCE-002",
            defectNo: "DEF-002",
            operationTaskPrefix: "OP-SOURCE-002");

        await HandleAsync(provider, CreateEvent());
        await HandleAsync(provider, CreateEvent(
            eventId: "evt-rework-002",
            ncrId: "ncr-002",
            ncrCode: "NCR-2026-0002",
            sourceDefectNo: "DEF-002",
            idempotencyKey: "quality:rework:org-001:env-dev:ncr-002"));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reworkWorkOrderIds = await db.WorkOrders
            .Where(x => x.WorkOrderType == WorkOrder.ReworkType)
            .Select(x => x.WorkOrderIdValue)
            .ToArrayAsync();
        Assert.Equal(2, reworkWorkOrderIds.Length);
        var operationTaskIds = await db.OperationTasks
            .Where(x => reworkWorkOrderIds.Contains(x.WorkOrderId))
            .Select(x => x.OperationTaskIdValue)
            .ToArrayAsync();
        Assert.Equal(4, operationTaskIds.Length);
        Assert.Equal(4, operationTaskIds.Distinct(StringComparer.Ordinal).Count());
    }

    [MesRealPostgresFact]
    public async Task Same_ncr_with_different_payload_is_dead_lettered_instead_of_treated_as_replay()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        var requestedAtUtc = DateTimeOffset.Parse("2026-08-29T08:00:00Z");
        await HandleAsync(provider, CreateEvent(requestedAtUtc: requestedAtUtc.AddTicks(9)));

        await HandleAsync(provider, CreateEvent(
            eventId: "evt-conflict",
            requestedAtUtc: requestedAtUtc.AddTicks(11)));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.WorkOrders.Where(x => x.SourceNcrId == "ncr-001").ToArrayAsync());
        var deadLetter = Assert.Single(await new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db)
            .ListAsync(
                NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None));
        Assert.Equal("mes.ncrReworkRequested.payloadConflict", deadLetter.FailureCode);
    }

    [MesRealPostgresFact]
    public async Task Missing_or_mismatched_mes_source_facts_fail_closed()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await HandleAsync(provider, CreateEvent(eventId: "evt-missing-defect"));
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await SeedDefectAsync(provider, "org-001", "env-dev", "DEF-OP-MISMATCH", "OP-OTHER");
        await HandleAsync(provider, CreateEvent(eventId: "evt-sku-mismatch", skuCode: "SKU-WRONG", idempotencyKey: "quality:rework:sku-wrong"));
        await HandleAsync(provider, CreateEvent(eventId: "evt-quantity-mismatch", quantity: 2m, idempotencyKey: "quality:rework:quantity-wrong"));
        await HandleAsync(provider, CreateEvent(
            eventId: "evt-operation-mismatch",
            sourceDefectNo: "DEF-OP-MISMATCH",
            idempotencyKey: "quality:rework:operation-wrong"));
        await SeedSourceWithoutRoutingAsync(provider, "org-route-missing", "env-dev");
        await HandleAsync(provider, CreateEvent(
            eventId: "evt-route-missing",
            organizationId: "org-route-missing",
            environmentId: "env-dev",
            sourceDefectNo: "DEF-NO-ROUTE",
            idempotencyKey: "quality:rework:route-missing"));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());
        var deadLetters = await new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db)
            .ListAsync(
                NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None);
        Assert.Equal(
            [
                "mes.ncrReworkRequested.sourceDefectMissing",
                "mes.ncrReworkRequested.skuMismatch",
                "mes.ncrReworkRequested.quantityMismatch",
                "mes.ncrReworkRequested.sourceOperationMismatch",
                "mes.ncrReworkRequested.sourceRoutingMissing",
            ],
            deadLetters.Select(x => x.FailureCode).ToArray());
    }

    [MesRealPostgresFact]
    public async Task Concurrent_delivery_serializes_on_ncr_scope_and_creates_exactly_one_work_order()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstHandler = CreateHandler(
            firstScope.ServiceProvider,
            new BlockingReworkScopeCoordinator(
                firstScope.ServiceProvider.GetRequiredService<IMesReworkWorkOrderScopeCoordinator>(),
                firstEntered,
                releaseFirst));
        var secondHandler = CreateHandler(secondScope.ServiceProvider);

        var firstTask = firstHandler.HandleAsync(CreateEvent(), CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = secondHandler.HandleAsync(CreateEvent(eventId: "evt-concurrent-002"), CancellationToken.None);
        await MesPostgresAdvisoryLockProbe.WaitForWaitersAsync(
            MesPostgresLaneDatabase.ConnectionString,
            expectedWaiters: 1,
            scopeDescription: "MES NCR rework creation");
        releaseFirst.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.WorkOrders.Where(x => x.SourceNcrId == "ncr-001").ToArrayAsync());
        Assert.Single(await db.CodeIdempotencyKeys.Where(x => x.IdempotencyKey == CreateEvent().IdempotencyKey).ToArrayAsync());
    }

    [MesRealPostgresFact]
    public async Task Same_ncr_identity_is_isolated_by_organization_and_environment()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await SeedSourceAsync(provider, "org-002", "env-test", workOrderLevelDefect: true);

        await HandleAsync(provider, CreateEvent());
        await HandleAsync(provider, CreateEvent(
            eventId: "evt-other-scope",
            organizationId: "org-002",
            environmentId: "env-test",
            idempotencyKey: "quality:rework:org-002:env-test:ncr-001"));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrders = await db.WorkOrders.Where(x => x.SourceNcrId == "ncr-001").ToArrayAsync();
        Assert.Equal(2, workOrders.Length);
        Assert.Contains(workOrders, x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev");
        Assert.Contains(workOrders, x => x.OrganizationId == "org-002" && x.EnvironmentId == "env-test");
        var workOrderLevel = workOrders.Single(x => x.OrganizationId == "org-002");
        var workOrderLevelTasks = await db.OperationTasks
            .Where(x => x.OrganizationId == "org-002" && x.WorkOrderId == workOrderLevel.WorkOrderIdValue)
            .OrderBy(x => x.OperationSequence)
            .ToArrayAsync();
        Assert.Equal([10, 20, 30], workOrderLevelTasks.Select(x => x.OperationSequence));
    }

    [MesRealPostgresFact]
    public async Task Rework_traceability_is_consistent_from_all_read_faces_and_keeps_audit_links_after_output_reversal_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = await CreateMigratedProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await HandleAsync(provider, CreateEvent());

        string reworkWorkOrderId;
        string reworkOperationTaskId;
        string reportNo;
        await using (var executionScope = provider.CreateAsyncScope())
        {
            var db = executionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reworkWorkOrder = await db.WorkOrders.SingleAsync(x => x.SourceNcrId == "ncr-001");
            reworkWorkOrderId = reworkWorkOrder.WorkOrderIdValue;
            var firstTask = await db.OperationTasks
                .OrderBy(x => x.OperationSequence)
                .FirstAsync(x => x.WorkOrderId == reworkWorkOrderId);
            reworkOperationTaskId = firstTask.OperationTaskIdValue;
            var reportedAtUtc = DateTimeOffset.Parse("2026-08-29T09:00:00Z");
            await new ChangeOperationTaskStateCommandHandler(db).Handle(
                new ChangeOperationTaskStateCommand(
                    "org-001",
                    "env-dev",
                    firstTask.OperationTaskIdValue,
                    "start",
                    reportedAtUtc.AddMinutes(-1),
                    "rework-trace:start"),
                CancellationToken.None);
            await db.SaveChangesAsync();
            var report = await new RecordProductionReportCommandHandler(
                    db,
                    TestProductionReportOeeDimensionSnapshotProvider.Instance,
                    executionScope.ServiceProvider.GetRequiredService<MesCodingService>())
                .Handle(
                    new RecordProductionReportCommand(
                        "org-001",
                        "env-dev",
                        reworkWorkOrderId,
                        firstTask.OperationTaskIdValue,
                        1m,
                        0m,
                        false,
                        reportedAtUtc,
                        "rework-trace:report",
                        ProducedLotNo: "LOT-001",
                        SerialNo: "SN-REWORK-001",
                        ReportedBy: "operator-rework"),
                    CancellationToken.None);
            await db.SaveChangesAsync();
            reportNo = report.ReportNo;

            db.WorkOrders.Add(WorkOrder.CreateRework(
                "org-foreign",
                "env-dev",
                "WO-REWORK-FOREIGN",
                "SKU-001",
                "PV-001",
                "PCS",
                3m,
                100,
                DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
                "WO-SOURCE-FOREIGN",
                null,
                "DEF-FOREIGN",
                "ncr-foreign",
                "NCR-FOREIGN",
                "LOT-001",
                "SN-001",
                DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
                "corr-foreign",
                "evt-foreign"));
            await db.SaveChangesAsync();
        }

        await using (var queryScope = provider.CreateAsyncScope())
        {
            var db = queryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var graphs = new[]
            {
                await new GetWorkOrderTraceabilityQueryHandler(db).Handle(
                    new GetWorkOrderTraceabilityQuery("org-001", "env-dev", "WO-SOURCE-001"),
                    CancellationToken.None),
                await new GetWorkOrderTraceabilityQueryHandler(db).Handle(
                    new GetWorkOrderTraceabilityQuery("org-001", "env-dev", reworkWorkOrderId),
                    CancellationToken.None),
                await new GetBatchTraceabilityQueryHandler(db).Handle(
                    new GetBatchTraceabilityQuery("org-001", "env-dev", "LOT-001"),
                    CancellationToken.None),
                await new GetBatchTraceabilityQueryHandler(db).Handle(
                    new GetBatchTraceabilityQuery("org-001", "env-dev", "SN-REWORK-001"),
                    CancellationToken.None),
                await new GetMaterialLotTraceabilityQueryHandler(db).Handle(
                    new GetMaterialLotTraceabilityQuery("org-001", "env-dev", "LOT-001"),
                    CancellationToken.None),
            };

            Assert.All(graphs, graph => AssertReworkChain(graph, reworkWorkOrderId, reportNo, expectOutput: true));

            var batchGraph = graphs[2];
            Assert.Contains(batchGraph.Edges, x =>
                x.FromNodeId == reportNo &&
                x.ToNodeId == reworkOperationTaskId &&
                x.RelationType == "reported-operation");
            Assert.Contains(batchGraph.Edges, x =>
                x.FromNodeId == reworkOperationTaskId &&
                x.ToNodeId == reworkWorkOrderId &&
                x.RelationType == "belongs-to-work-order");
            Assert.DoesNotContain(batchGraph.Edges, x =>
                x.FromNodeId == reworkWorkOrderId &&
                x.ToNodeId == reworkOperationTaskId &&
                x.RelationType == "has-operation");
            Assert.DoesNotContain(batchGraph.Edges, x =>
                x.FromNodeId == reworkOperationTaskId &&
                x.ToNodeId == reportNo &&
                x.RelationType == "has-report");

            var missingBatch = await new GetBatchTraceabilityQueryHandler(db).Handle(
                new GetBatchTraceabilityQuery("org-001", "env-dev", "LOT-NOT-FOUND"),
                CancellationToken.None);
            var unknownNode = Assert.Single(missingBatch.Nodes);
            Assert.Equal("LOT-NOT-FOUND", unknownNode.NodeId);
            Assert.Equal(MesTraceabilityNodeType.BatchOrSerial, unknownNode.NodeType);
            Assert.Equal("Unknown", unknownNode.Status);
            Assert.Empty(missingBatch.Edges);
        }

        await using (var reversalScope = provider.CreateAsyncScope())
        {
            var db = reversalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await new ReverseProductionReportCommandHandler(
                    db,
                    reversalScope.ServiceProvider.GetRequiredService<MesCodingService>())
                .Handle(
                    new ReverseProductionReportCommand(
                        "org-001",
                        "env-dev",
                        reportNo,
                        "返工产出数量有误",
                        DateTimeOffset.Parse("2026-08-29T09:10:00Z"),
                        "operator-rework",
                        "rework-trace:reverse"),
                    CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var afterReversalGraphs = new[]
        {
            await new GetWorkOrderTraceabilityQueryHandler(assertionDb).Handle(
                new GetWorkOrderTraceabilityQuery("org-001", "env-dev", "WO-SOURCE-001"),
                CancellationToken.None),
            await new GetWorkOrderTraceabilityQueryHandler(assertionDb).Handle(
                new GetWorkOrderTraceabilityQuery("org-001", "env-dev", reworkWorkOrderId),
                CancellationToken.None),
            await new GetBatchTraceabilityQueryHandler(assertionDb).Handle(
                new GetBatchTraceabilityQuery("org-001", "env-dev", "LOT-001"),
                CancellationToken.None),
            await new GetMaterialLotTraceabilityQueryHandler(assertionDb).Handle(
                new GetMaterialLotTraceabilityQuery("org-001", "env-dev", "LOT-001"),
                CancellationToken.None),
        };
        Assert.All(afterReversalGraphs, graph => AssertReworkChain(graph, reworkWorkOrderId, reportNo, expectOutput: false));
    }

    private static void AssertReworkChain(
        MesTraceabilityResponse graph,
        string reworkWorkOrderId,
        string reportNo,
        bool expectOutput)
    {
        Assert.Contains(graph.Nodes, x =>
            x.NodeId == "ncr-001" &&
            x.DisplayName == "NCR-2026-0001" &&
            x.NodeType == MesTraceabilityNodeType.NonconformanceReport);
        Assert.Contains(graph.Edges, x =>
            x.FromNodeId == "WO-SOURCE-001" &&
            x.ToNodeId == "ncr-001" &&
            x.RelationType == "raised-ncr");
        var sourceLotNode = Assert.Single(graph.Nodes, x =>
            x.DisplayName == "LOT-001" &&
            x.NodeType == MesTraceabilityNodeType.ProducedLot &&
            x.Status == "Source");
        Assert.Contains(graph.Edges, x =>
            x.FromNodeId == sourceLotNode.NodeId &&
            x.ToNodeId == "ncr-001" &&
            x.RelationType == "identified-in-ncr");
        Assert.Contains(graph.Edges, x =>
            x.FromNodeId == "SN-001" &&
            x.ToNodeId == "ncr-001" &&
            x.RelationType == "identified-in-ncr");
        Assert.Contains(graph.Edges, x =>
            x.FromNodeId == "ncr-001" &&
            x.ToNodeId == reworkWorkOrderId &&
            x.RelationType == "created-rework-work-order");
        Assert.DoesNotContain(graph.Nodes, x => x.NodeId == "ncr-foreign");
        Assert.Equal(
            graph.Nodes.Count,
            graph.Nodes.Select(x => new { x.NodeId, x.NodeType }).Distinct().Count());
        Assert.Equal(
            graph.Edges.Count,
            graph.Edges.Select(x => new { x.FromNodeId, x.ToNodeId, x.RelationType }).Distinct().Count());
        AssertAcyclic(graph.Edges);

        if (expectOutput)
        {
            Assert.Contains(graph.Edges, x =>
                x.FromNodeId == reportNo &&
                x.ToNodeId == "LOT-001" &&
                x.RelationType == "produced-lot");
            Assert.Contains(graph.Edges, x =>
                x.FromNodeId == reportNo &&
                x.ToNodeId == "SN-REWORK-001" &&
                x.RelationType == "produced-serial");
        }
        else
        {
            Assert.Contains(graph.Nodes, x =>
                x.NodeId == reportNo &&
                x.NodeType == MesTraceabilityNodeType.ProductionReport);
            Assert.DoesNotContain(graph.Edges, x =>
                x.FromNodeId == reportNo &&
                x.RelationType is "produced-lot" or "produced-serial");
        }
    }

    private static void AssertAcyclic(IReadOnlyCollection<MesTraceabilityEdge> edges)
    {
        var outgoing = edges
            .GroupBy(x => x.FromNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.ToNodeId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool HasCycle(string nodeId)
        {
            if (!visiting.Add(nodeId))
            {
                return true;
            }

            if (visited.Contains(nodeId))
            {
                visiting.Remove(nodeId);
                return false;
            }

            if (outgoing.TryGetValue(nodeId, out var targets) && targets.Any(HasCycle))
            {
                return true;
            }

            visiting.Remove(nodeId);
            visited.Add(nodeId);
            return false;
        }

        Assert.DoesNotContain(outgoing.Keys, HasCycle);
    }

    private static Task<ServiceProvider> CreateMigratedProviderAsync() =>
        CreateMigratedProviderAsync(NoRequirementsSnapshotProvider.Instance);

    private static async Task<ServiceProvider> CreateMigratedProviderAsync(
        IMesMaterialRequirementSnapshotProvider materialSnapshotProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator>(new NoopMediator());
        services.AddSingleton(materialSnapshotProvider);
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            MesPostgresLaneDatabase.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "mes")));
        services.AddScoped<MesCodingService>();
        services.AddScoped<IMesReworkWorkOrderScopeCoordinator, PostgreSqlMesReworkWorkOrderScopeCoordinator>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        return provider;
    }

    private static async Task SeedSourceAsync(
        IServiceProvider provider,
        string organizationId,
        string environmentId,
        string sourceWorkOrderId = "WO-SOURCE-001",
        string defectNo = "DEF-001",
        string operationTaskPrefix = "OP-SOURCE",
        bool workOrderLevelDefect = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sourceWorkOrder = WorkOrder.Create(
            organizationId,
            environmentId,
            sourceWorkOrderId,
            "SKU-001",
            "PV-001",
            10m,
            100,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            "PCS");
        var sourceOperations = new[]
        {
            OperationTask.Queue(
                organizationId,
                environmentId,
                sourceWorkOrderId,
                $"{operationTaskPrefix}-10",
                10,
                "WC-010",
                ["WC-010-B"],
                DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
                TimeSpan.FromMinutes(10),
                "SKU-001",
                "PCS",
                10m,
                false,
                "OP-CODE-010"),
            OperationTask.Create(
                organizationId,
                environmentId,
                sourceWorkOrderId,
                $"{operationTaskPrefix}-20",
                OperationTaskLifecycleStatus.Completed,
                20,
                "WC-020",
                ["WC-020-B", "WC-020-C"],
                DateTimeOffset.Parse("2026-08-29T08:10:00Z"),
                TimeSpan.FromMinutes(20),
                DateTimeOffset.Parse("2026-08-29T07:00:00Z"),
                DateTimeOffset.Parse("2026-08-29T07:20:00Z"),
                "SKU-001",
                "PCS",
                10m,
                true,
                "OP-CODE-020"),
            OperationTask.Create(
                organizationId,
                environmentId,
                sourceWorkOrderId,
                $"{operationTaskPrefix}-30",
                OperationTaskLifecycleStatus.InProgress,
                30,
                "WC-030",
                [],
                DateTimeOffset.Parse("2026-08-29T08:30:00Z"),
                TimeSpan.FromMinutes(30),
                DateTimeOffset.Parse("2026-08-29T07:20:00Z"),
                null,
                "SKU-001",
                "PCS",
                10m,
                false,
                "OP-CODE-030"),
        };
        sourceWorkOrder.MarkReleased(sourceOperations);
        db.WorkOrders.Add(sourceWorkOrder);
        db.OperationTasks.AddRange(sourceOperations);
        db.DefectRecords.Add(DefectRecord.Create(
            organizationId,
            environmentId,
            defectNo,
            sourceWorkOrderId,
            workOrderLevelDefect ? null : $"{operationTaskPrefix}-20",
            "surface-defect",
            3m,
            DateTimeOffset.Parse("2026-08-29T07:00:00Z")));
        await db.SaveChangesAsync();
    }

    private static async Task SeedSourceWithoutRoutingAsync(
        IServiceProvider provider,
        string organizationId,
        string environmentId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.WorkOrders.Add(WorkOrder.Create(
            organizationId,
            environmentId,
            "WO-SOURCE-NO-ROUTE",
            "SKU-001",
            "PV-001",
            10m,
            100,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            "PCS"));
        db.DefectRecords.Add(DefectRecord.Create(
            organizationId,
            environmentId,
            "DEF-NO-ROUTE",
            "WO-SOURCE-NO-ROUTE",
            null,
            "surface-defect",
            3m,
            DateTimeOffset.Parse("2026-08-29T07:00:00Z")));
        await db.SaveChangesAsync();
    }

    private static async Task HandleAsync(IServiceProvider provider, NcrReworkRequestedIntegrationEvent integrationEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        await CreateHandler(scope.ServiceProvider).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task SeedDefectAsync(
        IServiceProvider provider,
        string organizationId,
        string environmentId,
        string defectNo,
        string operationTaskId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.DefectRecords.Add(DefectRecord.Create(
            organizationId,
            environmentId,
            defectNo,
            "WO-SOURCE-001",
            operationTaskId,
            "surface-defect",
            3m,
            DateTimeOffset.Parse("2026-08-29T07:00:00Z")));
        await db.SaveChangesAsync();
    }

    private static NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder CreateHandler(
        IServiceProvider provider,
        IMesReworkWorkOrderScopeCoordinator? coordinator = null)
    {
        var db = provider.GetRequiredService<ApplicationDbContext>();
        return new NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder(
            db,
            provider.GetRequiredService<MesCodingService>(),
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db),
            provider.GetRequiredService<IMesMaterialRequirementSnapshotProvider>(),
            coordinator ?? provider.GetRequiredService<IMesReworkWorkOrderScopeCoordinator>());
    }

    private static NcrReworkRequestedIntegrationEvent CreateEvent(
        string eventId = "evt-rework-001",
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string ncrId = "ncr-001",
        string ncrCode = "NCR-2026-0001",
        string skuCode = "SKU-001",
        decimal quantity = 3m,
        string sourceDefectNo = "DEF-001",
        string idempotencyKey = "quality:rework:org-001:env-dev:ncr-001",
        DateTimeOffset? requestedAtUtc = null) => new(
            eventId,
            QualityIntegrationEventTypes.NcrReworkRequested,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            organizationId,
            environmentId,
            "user:quality-manager",
            idempotencyKey,
            new NcrReworkRequestedPayload(
                ncrId,
                ncrCode,
                sourceDefectNo,
                skuCode,
                quantity,
                "LOT-001",
                "SN-001",
                requestedAtUtc ?? DateTimeOffset.Parse("2026-08-29T08:00:00Z")));

    private sealed class BlockingReworkScopeCoordinator(
        IMesReworkWorkOrderScopeCoordinator inner,
        TaskCompletionSource firstEntered,
        TaskCompletionSource releaseFirst) : IMesReworkWorkOrderScopeCoordinator
    {
        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            string ncrId,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                ncrId,
                async token =>
                {
                    await action(token);
                    firstEntered.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                },
                cancellationToken);
    }

    private sealed class CapturingReworkScopeCoordinator(
        IMesReworkWorkOrderScopeCoordinator inner,
        ApplicationDbContext dbContext) : IMesReworkWorkOrderScopeCoordinator
    {
        public ReworkWorkOrderCreatedDomainEvent? Captured { get; private set; }

        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            string ncrId,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                ncrId,
                async token =>
                {
                    await action(token);
                    var workOrder = dbContext.ChangeTracker.Entries<WorkOrder>()
                        .Single(x => x.State == EntityState.Added && x.Entity.WorkOrderType == WorkOrder.ReworkType)
                        .Entity;
                    Captured = Assert.Single(workOrder.GetDomainEvents().OfType<ReworkWorkOrderCreatedDomainEvent>());
                },
                cancellationToken);
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StaticMaterialSnapshotProvider(MesMaterialRequirementSnapshotResult result)
        : IMesMaterialRequirementSnapshotProvider
    {
        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class NoRequirementsSnapshotProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoRequirementsSnapshotProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}
