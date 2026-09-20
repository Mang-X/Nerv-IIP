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
public sealed class PeriodicInspectionPostgresContinuationTests : PeriodicInspectionPostgresTestHarness
{
    [QualityPostgresFact]
    public async Task Completion_preserves_and_commits_the_terminal_257th_quantity_window_on_postgres()
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
        await HandleReportAsync(options, ProductionReport("RPT-257-COMPLETE", 257m, false, null, "2026-08-24T01:30:00Z"));
        await HandleCompletionAsync(options);

        PeriodicInspectionRuntimeContextId runtimeContextId;
        DateTime observedNextAttemptAtUtc;
        await using (var pendingAssertion = CreateContext(options))
        {
            var context = await pendingAssertion.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
            runtimeContextId = context.Id;
            observedNextAttemptAtUtc = context.QuantityContinuationNextAttemptAtUtc!.Value;
            Assert.Equal("closed", context.Status);
            Assert.Equal(256, context.LastGeneratedQuantityWindowSequence);
            Assert.NotNull(context.QuantityGenerationAnchorAtUtc);
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
        Assert.Equal("closed", finalContext.Status);
        Assert.Equal(257, finalContext.LastGeneratedQuantityWindowSequence);
        Assert.Null(finalContext.QuantityGenerationAnchorAtUtc);
        Assert.Null(finalContext.QuantityContinuationNextAttemptAtUtc);
        Assert.Equal(257, await finalAssertion.InspectionTasks.CountAsync());
        var terminalTask = await finalAssertion.InspectionTasks.AsNoTracking().SingleAsync(x => x.Quantity == 257m);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T01:30:00Z"), terminalTask.CreatedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-25T01:30:00Z"), terminalTask.DueAtUtc);
    }

    [QualityPostgresFact]
    public async Task Persisted_failure_deferral_exposes_the_101st_quantity_context_after_restart_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
        }

        await using (var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteSqlAsync(connection, """
                INSERT INTO quality.periodic_inspection_operations
                    (id, organization_id, environment_id, work_order_id, operation_id,
                     sku_code, operation_sequence, work_center_id, released_at_utc)
                SELECT lpad(i::text, 32, '0')::uuid,
                       'org-001', 'env-dev', format('WO-FAIR-%s', lpad(i::text, 3, '0')), 'OP-001',
                       'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z'
                FROM generate_series(1, 101) AS series(i);

                INSERT INTO quality.periodic_inspection_runtime_contexts
                    (id, operation_context_id, organization_id, environment_id, work_order_id, operation_id,
                     sku_code, operation_sequence, work_center_id, released_at_utc, inspection_plan_id,
                     inspection_plan_version, quantity_interval, assigned_team_id, first_activity_at_utc, uom_code,
                     cumulative_good_quantity, quantity_high_water, last_generated_quantity_window_sequence,
                     quantity_generation_anchor_at_utc, quantity_continuation_next_attempt_at_utc, status)
                SELECT lpad((i + 1000)::text, 32, '0')::uuid, lpad(i::text, 32, '0')::uuid,
                       'org-001', 'env-dev', format('WO-FAIR-%s', lpad(i::text, 3, '0')), 'OP-001',
                       'SKU-FG-1000', 10, 'WC-001', '2026-08-24T01:00:00Z',
                       lpad((i + 2000)::text, 32, '0')::uuid, 1, 1, 'team-quality-001',
                       '2026-08-24T01:10:00Z', 'EA', 257, 257, 256,
                       '2026-08-24T01:10:00Z', '2026-08-24T02:00:00Z', 'active'
                FROM generate_series(1, 101) AS series(i);
                """);
        }

        var nowUtc = DateTimeOffset.Parse("2026-08-24T02:00:00Z").UtcDateTime;
        PendingPeriodicInspectionQuantityContext poison;
        await using (var firstScan = CreateContext(options))
        {
            var candidates = await new ListPendingPeriodicInspectionQuantityContextsQueryHandler(firstScan).Handle(
                new ListPendingPeriodicInspectionQuantityContextsQuery(nowUtc, 100),
                CancellationToken.None);
            Assert.Equal(100, candidates.Count);
            Assert.DoesNotContain(candidates, candidate => candidate.WorkOrderId == "WO-FAIR-101");
            poison = candidates[0];
        }

        await using (var defer = CreateContext(options))
        {
            await new DeferPeriodicInspectionQuantityContinuationCommandHandler(
                defer,
                new PeriodicInspectionOperationScopeCoordinator(defer)).Handle(
                    new DeferPeriodicInspectionQuantityContinuationCommand(
                        poison.OrganizationId,
                        poison.EnvironmentId,
                        poison.WorkOrderId,
                        poison.OperationId,
                        poison.RuntimeContextId,
                        poison.ObservedNextAttemptAtUtc,
                        nowUtc.AddMinutes(1)),
                    CancellationToken.None);
        }

        await using var restartedScan = CreateContext(options);
        var restartedCandidates = await new ListPendingPeriodicInspectionQuantityContextsQueryHandler(restartedScan).Handle(
            new ListPendingPeriodicInspectionQuantityContextsQuery(nowUtc, 100),
            CancellationToken.None);
        Assert.Equal(100, restartedCandidates.Count);
        Assert.DoesNotContain(restartedCandidates, candidate => candidate.RuntimeContextId == poison.RuntimeContextId);
        Assert.Contains(restartedCandidates, candidate => candidate.WorkOrderId == "WO-FAIR-101");
    }

    [QualityPostgresFact]
    public async Task Oversized_legal_quantity_backlog_fails_closed_without_partial_state_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan(quantityInterval: 0.000001m));
            await setup.SaveChangesAsync();
        }

        await HandleReleaseAsync(options);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var report = ProductionReport(
            "RPT-OVERSIZED-LEGAL",
            2147.483648m,
            false,
            null,
            "2026-08-24T01:30:00Z");
        await using (var reportDb = CreateContext(options))
        {
            await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
                reportDb,
                new PeriodicInspectionOperationScopeCoordinator(reportDb),
                deadLetters).HandleAsync(report, CancellationToken.None);
        }

        await using var assertion = CreateContext(options);
        var context = await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking().SingleAsync();
        Assert.Empty(await assertion.PeriodicInspectionProductionReports.AsNoTracking().ToArrayAsync());
        Assert.Empty(await assertion.InspectionTasks.AsNoTracking().ToArrayAsync());
        Assert.Equal(0m, context.QuantityHighWater);
        Assert.Equal(0, context.LastGeneratedQuantityWindowSequence);
        Assert.Null(context.QuantityGenerationAnchorAtUtc);
        Assert.Null(context.QuantityContinuationNextAttemptAtUtc);
        Assert.Equal(1, await assertion.ProcessedIntegrationEvents.CountAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Contains("supported pending-window limit", deadLetter.FailureMessage, StringComparison.Ordinal);
    }
}
