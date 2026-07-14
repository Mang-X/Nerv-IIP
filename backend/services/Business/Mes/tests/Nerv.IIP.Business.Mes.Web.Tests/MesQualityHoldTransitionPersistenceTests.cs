using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesQualityHoldTransitionPersistenceTests
{
    [Fact]
    public async Task Quality_hold_transitions_round_trip_through_relational_persistence()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var occurredAtUtc = DateTimeOffset.Parse("2026-07-13T02:00:00Z");
        dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org-001", "env-dev", "business-mes", "WO-RELATIONAL", "QI-REL-1", "corr-rel-1",
            "hold-applied", "quality-user", occurredAtUtc, "measured defect", "QI-REL-1", "PLAN-REL", "automatic"));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.QualityHoldTransitions.SingleAsync();

        Assert.NotEqual(default, persisted.Id);
        Assert.Equal("WO-RELATIONAL", persisted.SourceDocumentId);
        Assert.Equal(occurredAtUtc, persisted.OccurredAtUtc);
        Assert.Equal("measured defect", persisted.Reason);
    }

    [Fact]
    public async Task Quality_hold_transition_identity_scopes_same_correlation_by_source_and_cycle()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await using var competingContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-07-13T03:00:00Z");
        dbContext.QualityHoldTransitions.AddRange(
            QualityHoldTransition.Record("org", "env", "source-a", "DOC-1", "cycle-1", "corr-shared", "hold-applied", "actor", now, null, "QI-1", null, "automatic"),
            QualityHoldTransition.Record("org", "env", "source-b", "DOC-1", "cycle-1", "corr-shared", "hold-applied", "actor", now, null, "QI-2", null, "automatic"),
            QualityHoldTransition.Record("org", "env", "source-a", "DOC-1", "cycle-2", "corr-shared", "hold-applied", "actor", now, null, "QI-3", null, "automatic"));

        await dbContext.SaveChangesAsync();

        Assert.Equal(3, await dbContext.QualityHoldTransitions.CountAsync());
    }

    [Fact]
    public async Task Quality_hold_manual_transition_replay_with_same_payload_is_idempotent_at_uow_boundary()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await using var competingContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-07-13T03:30:00Z");
        dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "DOC", "cycle", "corr-1", "manual-force-released", "actor", now,
            "reason", "QI", "PLAN", "manual", "idem-1"));
        await dbContext.SaveChangesAsync();
        competingContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "DOC", "cycle", "corr-1", "manual-force-released", "actor", now,
            "reason", "QI", "PLAN", "manual", "idem-1"));

        await competingContext.SaveChangesAsync();

        Assert.Single(await dbContext.QualityHoldTransitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Quality_hold_manual_transition_same_key_with_different_payload_is_governed_conflict()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await using var competingContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-07-13T03:40:00Z");
        dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "DOC", "cycle", "corr-1", "manual-force-released", "actor", now,
            "approved reason", "QI", "PLAN", "manual", "idem-collision"));
        await dbContext.SaveChangesAsync();
        competingContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "DOC", "cycle", "corr-2", "manual-force-released", "actor", now,
            "different reason", "QI", "PLAN", "manual", "idem-collision"));

        var exception = await Assert.ThrowsAsync<KnownException>(() => competingContext.SaveChangesAsync());

        Assert.Contains("idempotency", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quality_hold_idempotent_loser_does_not_overwrite_newer_current_projection()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using (var seed = CreateSqliteDbContext(connection))
        {
            await seed.Database.EnsureCreatedAsync();
            var start = DateTimeOffset.Parse("2026-07-13T06:00:00Z");
            seed.WorkOrders.Add(WorkOrder.Create("org", "env", "WO-RACE", "SKU", "PV", 1m, 1, start.AddHours(1)));
            seed.QualityHoldContexts.Add(QualityHoldContext.Capture(
                "org", "env", "WO-RACE", null, "source", "WO-RACE", "QI-1", "PLAN-1",
                "rejected", "quality.InspectionRejected", "first defect", start, "quality"));
            await seed.SaveChangesAsync();
        }

        await using var winner = CreateSqliteDbContext(connection);
        await using var loser = CreateSqliteDbContext(connection);
        var winnerHold = await winner.QualityHoldContexts.SingleAsync();
        var loserHold = await loser.QualityHoldContexts.SingleAsync();
        var releaseAt = DateTimeOffset.Parse("2026-07-13T06:05:00Z");
        winnerHold.ForceRelease("approved", "user:qa", releaseAt);
        loserHold.ForceRelease("approved", "user:qa", releaseAt);
        winner.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "WO-RACE", "QI-1", "corr-release", "manual-force-released",
            "user:qa", releaseAt, "approved", "QI-1", "PLAN-1", "manual", "idem-release"));
        loser.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "WO-RACE", "QI-1", "corr-release", "manual-force-released",
            "user:qa", releaseAt, "approved", "QI-1", "PLAN-1", "manual", "idem-release"));
        await winner.SaveChangesAsync();

        var newerAt = releaseAt.AddMinutes(1);
        winnerHold.ApplyInspectionResult("QI-2", "PLAN-2", "rejected", "quality.InspectionRejected", "new defect", newerAt, "quality");
        winner.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source", "WO-RACE", "QI-2", "corr-new-hold", "hold-applied",
            "quality", newerAt, "new defect", "QI-2", "PLAN-2", "automatic", "idem-new-hold"));
        await winner.SaveChangesAsync();

        await loser.SaveChangesAsync();

        await using var verify = CreateSqliteDbContext(connection);
        var current = await verify.QualityHoldContexts.SingleAsync();
        Assert.True(current.Active);
        Assert.Equal("QI-2", current.HeldInspectionRecordId);
        Assert.Equal("PLAN-2", current.HeldInspectionDocumentId);
        Assert.Equal(newerAt, current.RecordedAtUtc);
        Assert.Equal(2, await verify.QualityHoldTransitions.CountAsync());
    }

    [Fact]
    public async Task Quality_hold_timeline_relationally_orders_two_cycles_and_isolates_source_service()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-07-13T04:00:00Z");
        var kinds = new[] { "hold-applied", "inspection-released", "hold-applied", "manual-force-released" };
        for (var index = 0; index < kinds.Length; index++)
        {
            var cycle = index < 2 ? "cycle-1" : "cycle-2";
            dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
                "org", "env", "source-a", "DOC", cycle, $"corr-{index}", kinds[index], "actor",
                now.AddMinutes(index), $"reason-{index}", $"QI-{index}", $"PLAN-{cycle}",
                index == 3 ? "manual" : "automatic", $"event-idem-{index}"));
        }
        dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            "org", "env", "source-b", "DOC", "foreign-cycle", "foreign-corr", "hold-applied", "actor",
            now, null, "QI-X", null, "automatic"));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var result = await new GetQualityHoldTimelineQueryHandler(dbContext).Handle(
            new GetQualityHoldTimelineQuery("org", "env", "source-a", "DOC"), CancellationToken.None);

        Assert.Equal(kinds, result.Items.Select(x => x.EventKind));
        Assert.Equal(2, result.Items.Select(x => x.HoldCycleId).Distinct().Count());
        Assert.All(result.Items, x => Assert.Equal("source-a", x.SourceService));
        Assert.All(result.Items, x => Assert.False(string.IsNullOrWhiteSpace(x.SourceInspectionRecordId)));
        Assert.All(result.Items, x => Assert.False(string.IsNullOrWhiteSpace(x.SourceInspectionDocumentId)));
        Assert.Equal(Enumerable.Range(0, 4).Select(x => $"event-idem-{x}"), result.Items.Select(x => x.IdempotencyKey));
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
