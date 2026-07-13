using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class TelemetryProductionReportCandidatePostgresTests
{
    [MesRealPostgresFact]
    public async Task Status_scope_time_predicates_and_source_uniqueness_are_enforced_by_postgres()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        var start = DateTimeOffset.Parse("2026-07-12T01:00:00Z");
        db.TelemetryProductionReportCandidates.Add(TelemetryProductionReportCandidate.CreateDraft("org-001", "env-dev", "source-001", "DEV-01", "count", 2m, start, start.AddMinutes(1), "WC-01", "WO-01", "OP-10"));
        db.TelemetryProductionReportCandidates.Add(TelemetryProductionReportCandidate.CreatePendingConfirmation("org-001", "env-dev", "source-002", "DEV-02", "count", "posted", 3m, start.AddMinutes(2), start.AddMinutes(3), null, null, null, TelemetryProductionReportCandidate.NoWorkCenterMappingSuspensionReason));
        await db.SaveChangesAsync();

        var result = await new ListTelemetryProductionReportCandidatesQueryHandler(db).Handle(
            new("org-001", "env-dev", "pending-confirmation", null, "DEV-02", start.AddMinutes(1), start.AddMinutes(4), 0, 20), CancellationToken.None);
        Assert.Single(result.Items);
        Assert.Equal("source-002", result.Items.Single().SourceIdempotencyKey);

        var confirmedCandidate = await db.TelemetryProductionReportCandidates.SingleAsync(x => x.SourceIdempotencyKey == "source-001");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-01", "SKU-01", "PV-01", 10m, 1, start.AddHours(1), "PCS");
        workOrder.MarkReleased(); workOrder.Start(start);
        var operation = OperationTask.Create("org-001", "env-dev", "WO-01", "OP-10", OperationTaskLifecycleStatus.InProgress, 10, "WC-01", [], start, TimeSpan.FromHours(1), start, null);
        db.WorkOrders.Add(workOrder); db.OperationTasks.Add(operation);
        await db.SaveChangesAsync();
        var report = ProductionReport.Record("org-001", "env-dev", "PR-PG-001", "WO-01", "OP-10", 2m, 0m, false, start.AddMinutes(1), source: ProductionReport.TelemetrySource);
        db.ProductionReports.Add(report);
        await db.SaveChangesAsync();
        confirmedCandidate.Confirm("WO-01", "OP-10", "operator:pg", start.AddMinutes(2), report.Id.ToString());
        await db.SaveChangesAsync();
        var replay = await new PromoteTelemetryProductionReportCandidateCommandHandler(db, new ThrowingSender()).Handle(
            new("org-001", "env-dev", confirmedCandidate.Id, "WO-01", "OP-10", "operator:pg", start.AddMinutes(3)), CancellationToken.None);
        Assert.Equal(report.Id, replay.Id);

        db.TelemetryProductionReportCandidates.Add(TelemetryProductionReportCandidate.CreateDraft("org-001", "env-dev", "source-001", "DEV-03", "count", 1m, start, start.AddMinutes(1), "WC-01", "WO-01", "OP-10"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException("Confirmed replay must not invoke RecordProductionReportCommand.");
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [MesRealPostgresFact]
    public async Task Scheduled_at_utc_migration_backfill_populates_aps_placed_tasks_only()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();

        var due = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        db.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-BF-01", "FG", "PV", 1m, 10, due, "PCS", null));
        var scheduledAt = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
        var dispatchAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z");

        // APS-placed, not dispatched: ApplyScheduleAssignment sets assigned_at_utc (the schedule time) and no operator.
        var apsTask = OperationTask.Queue("org-001", "env-dev", "WO-BF-01", "OP-APS", 10, "WC-1", [], scheduledAt, TimeSpan.FromMinutes(30));
        apsTask.ApplyScheduleAssignment("WC-1", "DEV-1", scheduledAt, scheduledAt.AddMinutes(30), scheduledAt);
        // Manually dispatched: Assign sets an operator and overwrites assigned_at_utc with the dispatch time.
        var dispatchedTask = OperationTask.Queue("org-001", "env-dev", "WO-BF-01", "OP-DISPATCH", 20, "WC-1", [], scheduledAt, TimeSpan.FromMinutes(30));
        dispatchedTask.Assign("operator-1", "DEV-1", "shift-a", dispatchAt);
        // Never scheduled or dispatched.
        var queuedTask = OperationTask.Queue("org-001", "env-dev", "WO-BF-01", "OP-QUEUED", 30, "WC-1", [], scheduledAt, TimeSpan.FromMinutes(30));
        db.OperationTasks.AddRange(apsTask, dispatchedTask, queuedTask);
        await db.SaveChangesAsync();

        // Simulate the pre-migration state (the scheduled_at_utc column did not exist) then run the migration's
        // backfill statement verbatim.
        await db.Database.ExecuteSqlRawAsync("UPDATE mes.operation_tasks SET scheduled_at_utc = NULL;");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE mes.operation_tasks SET scheduled_at_utc = assigned_at_utc WHERE assigned_at_utc IS NOT NULL AND assigned_user_id IS NULL;");

        db.ChangeTracker.Clear();
        var aps = await db.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-APS");
        var dispatched = await db.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-DISPATCH");
        var queued = await db.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-QUEUED");
        Assert.Equal(scheduledAt, aps.ScheduledAtUtc);
        Assert.Null(dispatched.ScheduledAtUtc);
        Assert.Null(queued.ScheduledAtUtc);
    }

    private sealed class TemporaryDatabase(string adminConnectionString, string databaseName, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;
        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var name = $"nerv_mes_candidates_{Guid.NewGuid():N}";
            var admin = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;
            await using var connection = new NpgsqlConnection(admin); await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync();
            return new(admin, name, new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = name }.ConnectionString);
        }
        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString); await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal sealed class MesRealPostgresFactAttribute : FactAttribute
{
    public MesRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))) Skip = "Set NERV_IIP_TEST_POSTGRES to run real PostgreSQL MES candidate proof.";
    }
}
