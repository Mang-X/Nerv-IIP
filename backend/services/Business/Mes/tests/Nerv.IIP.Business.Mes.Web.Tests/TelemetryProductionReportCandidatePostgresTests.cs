using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class TelemetryProductionReportCandidatePostgresTests
{
    [MesRealPostgresFact]
    public async Task Status_scope_time_predicates_and_source_uniqueness_are_enforced_by_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
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
