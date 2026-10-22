using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class PeriodicInspectionPostgresConcurrencyTests : PeriodicInspectionPostgresTestHarness
{
    [QualityPostgresFact]
    public async Task Postgres_out_of_order_reversal_duplicate_close_and_restart_converge_without_tasks()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        await HandleReportAsync(options, ProductionReport("RPT-REV-001", -30m, true, "RPT-001", "2026-08-24T01:20:00Z"));
        await HandleReportAsync(options, ProductionReport("RPT-001", 100m, false, null, "2026-08-24T01:10:00Z"));
        await HandleReportAsync(options, ProductionReport("RPT-001", 100m, false, null, "2026-08-24T01:10:00Z"));
        await HandleCompletionAsync(options);
        await HandleReleaseAsync(options);

        await using var assertion = CreateContext(options);
        var operation = await assertion.PeriodicInspectionOperations
            .AsNoTracking()
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var context = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(2, operation.ProductionReports.Count);
        Assert.Equal(70m, context.CumulativeGoodQuantity);
        Assert.Equal(100m, context.QuantityHighWater);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T01:10:00Z").UtcDateTime, context.FirstActivityAtUtc);
        Assert.Equal("closed", context.Status);
        Assert.Empty(await assertion.InspectionTasks.ToListAsync());
    }

    [QualityPostgresFact]
    public async Task Postgres_operation_scope_lock_serializes_concurrent_duplicate_source_creation()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
        }

        await using var firstContext = CreateContext(options);
        await using var secondContext = CreateContext(options);
        var firstCoordinator = new PeriodicInspectionOperationScopeCoordinator(firstContext);
        var secondCoordinator = new PeriodicInspectionOperationScopeCoordinator(secondContext);
        var firstHoldingLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = firstCoordinator.ExecuteAsync(
            "org-001", "env-dev", "WO-CONCURRENT", ["OP-001"],
            async cancellationToken =>
            {
                firstContext.PeriodicInspectionOperations.Add(
                    PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-CONCURRENT", "OP-001"));
                firstHoldingLock.SetResult();
                await allowFirstCommit.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);
        await firstHoldingLock.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = secondCoordinator.ExecuteAsync(
            "org-001", "env-dev", "WO-CONCURRENT", ["OP-001"],
            async cancellationToken =>
            {
                var exists = await secondContext.PeriodicInspectionOperations.AnyAsync(
                    x => x.OrganizationId == "org-001"
                        && x.EnvironmentId == "env-dev"
                        && x.WorkOrderId == "WO-CONCURRENT"
                        && x.OperationId == "OP-001",
                    cancellationToken);
                if (!exists)
                {
                    secondContext.PeriodicInspectionOperations.Add(
                        PeriodicInspectionOperation.CreatePending("org-001", "env-dev", "WO-CONCURRENT", "OP-001"));
                }
            },
            CancellationToken.None);

        await WaitForAdvisoryWaitersAsync();
        Assert.False(second.IsCompleted, "The competing operation must be observably parked on the advisory lock.");
        allowFirstCommit.SetResult();
        await Task.WhenAll(first, second);

        await using var assertion = CreateContext(options);
        Assert.Equal(1, await assertion.PeriodicInspectionOperations.CountAsync());
    }

    [QualityPostgresFact]
    public async Task Concurrent_time_generation_creates_one_window_task_and_atomically_advances_watermark_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        await HandleReleaseAsync(options);
        await HandleReportAsync(options, ProductionReport("RPT-TIME-001", 25m, false, null, "2026-08-24T01:30:00Z"));

        await using var gateConnection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await gateConnection.OpenAsync();
        await using var gateTransaction = await gateConnection.BeginTransactionAsync();
        await using (var gateCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
            gateConnection,
            gateTransaction))
        {
            gateCommand.Parameters.AddWithValue(
                "key",
                "quality-periodic-inspection:org-001:env-dev:WO-001:OP-001");
            await gateCommand.ExecuteNonQueryAsync();
        }

        await using var firstContext = CreateContext(options);
        await using var secondContext = CreateContext(options);
        var runtimeContextId = await firstContext.PeriodicInspectionRuntimeContexts
            .AsNoTracking()
            .Select(x => x.Id)
            .SingleAsync();
        var command = new GeneratePeriodicInspectionTimeTaskForContextCommand(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-001",
            runtimeContextId,
            DateTimeOffset.Parse("2026-08-24T03:30:00Z").UtcDateTime,
            24);
        var first = new GeneratePeriodicInspectionTimeTaskForContextCommandHandler(
            firstContext,
            new PeriodicInspectionOperationScopeCoordinator(firstContext)).Handle(command, CancellationToken.None);
        var second = new GeneratePeriodicInspectionTimeTaskForContextCommandHandler(
            secondContext,
            new PeriodicInspectionOperationScopeCoordinator(secondContext)).Handle(command, CancellationToken.None);

        await WaitForAdvisoryWaitersAsync(expected: 2, competingTasks: [first, second]);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await gateTransaction.CommitAsync();

        var generatedCounts = await Task.WhenAll(first, second);
        Assert.Equal([0, 1], generatedCounts.Order().ToArray());

        await using var assertion = CreateContext(options);
        var task = await assertion.InspectionTasks.AsNoTracking().SingleAsync();
        var runtimeContext = await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
        Assert.Equal($"OP-001:periodic-time:{runtimeContext.Id.Id:D}:1", task.SourceDocumentLineId);
        Assert.Equal($"quality:periodic-time:{runtimeContext.Id.Id:D}:1", task.TriggerIdempotencyKey);
        Assert.Equal(1, runtimeContext.LastGeneratedTimeWindowSequence);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T01:30:00Z").UtcDateTime, runtimeContext.TimeScheduleAnchorAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T05:30:00Z").UtcDateTime, runtimeContext.NextTimeWindowAtUtc);
    }

    [QualityPostgresFact]
    public async Task Concurrent_quantity_reports_create_each_window_once_and_atomically_advance_watermark_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        await HandleReleaseAsync(options);
        await using var gateConnection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await gateConnection.OpenAsync();
        await using var gateTransaction = await gateConnection.BeginTransactionAsync();
        await using (var gateCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
            gateConnection,
            gateTransaction))
        {
            gateCommand.Parameters.AddWithValue(
                "key",
                "quality-periodic-inspection:org-001:env-dev:WO-001:OP-001");
            await gateCommand.ExecuteNonQueryAsync();
        }

        var first = HandleReportAsync(
            options,
            ProductionReport("RPT-QTY-001", 100m, false, null, "2026-08-24T01:30:00Z"));
        var second = HandleReportAsync(
            options,
            ProductionReport("RPT-QTY-002", 100m, false, null, "2026-08-24T01:31:00Z"));

        await WaitForAdvisoryWaitersAsync(expected: 2, competingTasks: [first, second]);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await gateTransaction.CommitAsync();
        await Task.WhenAll(first, second);

        await using var assertion = CreateContext(options);
        var context = await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
        var reports = await assertion.PeriodicInspectionProductionReports.AsNoTracking().OrderBy(x => x.ReportNo).ToArrayAsync();
        var tasks = await assertion.InspectionTasks.AsNoTracking().OrderBy(x => x.Quantity).ToArrayAsync();
        Assert.Equal(["RPT-QTY-001", "RPT-QTY-002"], reports.Select(x => x.ReportNo));
        Assert.Equal([100m, 200m], tasks.Select(x => x.Quantity));
        Assert.Equal(2, context.LastGeneratedQuantityWindowSequence);
        Assert.Equal(200m, context.QuantityHighWater);
        Assert.Equal(2, tasks.Select(x => x.TriggerIdempotencyKey).Distinct().Count());
    }

    [QualityPostgresFact]
    public async Task Concurrent_same_event_id_uses_one_inbox_fact_and_resumes_a_supported_bounded_backlog_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan(quantityInterval: 1m));
            await setup.SaveChangesAsync();
        }

        await HandleReleaseAsync(options);
        var first = ProductionReport(
            "RPT-BOUNDARY-A",
            257m,
            false,
            null,
            "2026-08-24T01:30:00Z");
        var second = ProductionReport(
            "RPT-BOUNDARY-B",
            257m,
            false,
            null,
            "2026-08-24T01:31:00Z") with
        {
            EventId = first.EventId,
        };

        await Task.WhenAll(
            HandleReportAsync(options, first),
            HandleReportAsync(options, second));

        PeriodicInspectionRuntimeContextId runtimeContextId;
        DateTime observedNextAttemptAtUtc;
        await using (var firstAssertion = CreateContext(options))
        {
            var operation = await firstAssertion.PeriodicInspectionOperations
                .AsNoTracking()
                .Include(x => x.ProductionReports)
                .Include(x => x.RuntimeContexts)
                .SingleAsync();
            Assert.Single(operation.ProductionReports);
            var runtimeContext = Assert.Single(operation.RuntimeContexts);
            runtimeContextId = runtimeContext.Id;
            observedNextAttemptAtUtc = runtimeContext.QuantityContinuationNextAttemptAtUtc!.Value;
            Assert.Equal(256, runtimeContext.LastGeneratedQuantityWindowSequence);
            Assert.NotNull(runtimeContext.QuantityGenerationAnchorAtUtc);
            Assert.Equal(256, await firstAssertion.InspectionTasks.CountAsync());
            Assert.Equal(2, await firstAssertion.ProcessedIntegrationEvents.CountAsync());
        }

        await using (var continuation = CreateContext(options))
        {
            var generated = await new GeneratePeriodicInspectionQuantityTaskBatchForContextCommandHandler(
                continuation,
                new PeriodicInspectionOperationScopeCoordinator(continuation)).Handle(
                    new GeneratePeriodicInspectionQuantityTaskBatchForContextCommand(
                        "org-001",
                        "env-dev",
                        "WO-001",
                        "OP-001",
                        runtimeContextId,
                        observedNextAttemptAtUtc,
                        observedNextAttemptAtUtc.AddMinutes(1),
                        256),
                    CancellationToken.None);
            Assert.Equal(1, generated);
        }

        await using var finalAssertion = CreateContext(options);
        var finalContext = await finalAssertion.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
        Assert.Equal(257, finalContext.LastGeneratedQuantityWindowSequence);
        Assert.Null(finalContext.QuantityGenerationAnchorAtUtc);
        Assert.Null(finalContext.QuantityContinuationNextAttemptAtUtc);
        Assert.Equal(257, await finalAssertion.InspectionTasks.CountAsync());
        Assert.Equal(
            [1m, 257m],
            await finalAssertion.InspectionTasks
                .OrderBy(x => x.Quantity)
                .Select(x => x.Quantity)
                .Where(x => x == 1m || x == 257m)
                .ToArrayAsync());
    }

    [QualityPostgresFact]
    public async Task Same_event_id_for_different_operations_waits_on_the_inbox_lock_and_commits_one_payload_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        var release = WorkOrderReleased() with
        {
            EventId = "evt-release-two-operations",
            IdempotencyKey = "mes:work-order-released:org-001:env-dev:WO-001:two-operations",
            Payload = WorkOrderReleased().Payload with
            {
                Operations =
                [
                    new ReleasedOperationPayload("OP-001", 10, "WC-001"),
                    new ReleasedOperationPayload("OP-002", 20, "WC-001"),
                ],
            },
        };
        await using (var releaseDb = CreateContext(options))
        {
            await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
                releaseDb,
                new PeriodicInspectionOperationScopeCoordinator(releaseDb),
                new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(release, CancellationToken.None);
        }

        var first = ProductionReport("RPT-EVENT-LOCK-A", 100m, false, null, "2026-08-24T01:30:00Z") with
        {
            EventId = "evt-report-shared-across-operations",
        };
        var second = ProductionReport("RPT-EVENT-LOCK-B", 100m, false, null, "2026-08-24T01:31:00Z") with
        {
            EventId = first.EventId,
            Payload = ProductionReport("RPT-EVENT-LOCK-B", 100m, false, null, "2026-08-24T01:31:00Z").Payload with
            {
                OperationTaskId = "OP-002",
            },
        };

        await using var gateConnection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await gateConnection.OpenAsync();
        await using var gateTransaction = await gateConnection.BeginTransactionAsync();
        await using (var gateCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
            gateConnection,
            gateTransaction))
        {
            gateCommand.Parameters.AddWithValue(
                "key",
                $"quality-integration-event-inbox:{ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection.ConsumerName}:{first.EventId}");
            await gateCommand.ExecuteNonQueryAsync();
        }

        var firstTask = HandleReportAsync(options, first);
        var secondTask = HandleReportAsync(options, second);
        await WaitForAdvisoryWaitersAsync(expected: 2, competingTasks: [firstTask, secondTask]);
        Assert.False(firstTask.IsCompleted);
        Assert.False(secondTask.IsCompleted);
        await gateTransaction.CommitAsync();
        await Task.WhenAll(firstTask, secondTask);

        await using var assertion = CreateContext(options);
        Assert.Equal(1, await assertion.PeriodicInspectionProductionReports.CountAsync());
        Assert.Equal(1, await assertion.InspectionTasks.CountAsync());
        Assert.Equal(2, await assertion.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(1, await assertion.PeriodicInspectionRuntimeContexts.CountAsync(x => x.LastGeneratedQuantityWindowSequence == 1));
        Assert.Equal(1, await assertion.PeriodicInspectionRuntimeContexts.CountAsync(x => x.LastGeneratedQuantityWindowSequence == 0));
    }

    [QualityPostgresFact]
    public async Task Quantity_task_write_failure_rolls_back_report_watermark_and_task_before_replay_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        await HandleReleaseAsync(options);
        await using (var releaseAssertion = CreateContext(options))
        {
            Assert.Single(await releaseAssertion.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        }
        await using (var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteSqlAsync(connection, """
                CREATE OR REPLACE FUNCTION quality.fail_periodic_quantity_task()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.source_document_line_id LIKE '%:periodic-quantity:%' THEN
                        RAISE EXCEPTION 'injected periodic quantity task failure';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER fail_periodic_quantity_task
                BEFORE INSERT ON quality.inspection_tasks
                FOR EACH ROW EXECUTE FUNCTION quality.fail_periodic_quantity_task();
                """);
        }

        var report = ProductionReport("RPT-QTY-FAIL", 100m, false, null, "2026-08-24T01:30:00Z");
        await Assert.ThrowsAsync<DbUpdateException>(() => HandleReportAsync(options, report));

        await using (var failedAssertion = CreateContext(options))
        {
            var context = await failedAssertion.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
            Assert.Empty(await failedAssertion.PeriodicInspectionProductionReports.AsNoTracking().ToArrayAsync());
            Assert.Empty(await failedAssertion.InspectionTasks.AsNoTracking().ToArrayAsync());
            Assert.Single(await failedAssertion.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
            Assert.Equal(0, context.LastGeneratedQuantityWindowSequence);
            Assert.Equal(0m, context.QuantityHighWater);
        }

        await using (var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteSqlAsync(connection, """
                DROP TRIGGER fail_periodic_quantity_task ON quality.inspection_tasks;
                DROP FUNCTION quality.fail_periodic_quantity_task();
                """);
        }

        await HandleReportAsync(options, report);
        await using var replayAssertion = CreateContext(options);
        Assert.Single(await replayAssertion.PeriodicInspectionProductionReports.AsNoTracking().ToArrayAsync());
        Assert.Single(await replayAssertion.InspectionTasks.AsNoTracking().ToArrayAsync());
        Assert.Equal(2, await replayAssertion.ProcessedIntegrationEvents.AsNoTracking().CountAsync());
        Assert.Equal(
            1,
            await replayAssertion.PeriodicInspectionRuntimeContexts.AsNoTracking()
                .Select(x => x.LastGeneratedQuantityWindowSequence)
                .SingleAsync());
    }

    [QualityPostgresFact]
    public async Task Production_mediator_uow_dispatches_one_context_command_and_commits_its_task()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }
        await HandleReleaseAsync(options);
        await HandleReportAsync(options, ProductionReport("RPT-UOW-001", 25m, false, null, "2026-08-24T01:30:00Z"));

        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddQualityPostgreSqlPersistence(QualityPostgresLaneDatabase.ConnectionString);
        services.AddIntegrationEvents(typeof(Program));
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var context = await dbContext.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
            var generated = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new GeneratePeriodicInspectionTimeTaskForContextCommand(
                    "org-001",
                    "env-dev",
                    "WO-001",
                    "OP-001",
                    context.Id,
                    DateTimeOffset.Parse("2026-08-24T03:30:00Z").UtcDateTime,
                    24),
                CancellationToken.None);
            Assert.Equal(1, generated);
        }

        await using var assertion = CreateContext(options);
        Assert.Single(await assertion.InspectionTasks.AsNoTracking().ToArrayAsync());
        Assert.Equal(
            1,
            await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking()
                .Select(x => x.LastGeneratedTimeWindowSequence)
                .SingleAsync());
    }
}
