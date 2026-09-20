using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// ProviderBehavior / DomainInvariant: #3650。真实 migration、唯一约束及 stale 写入竞争。
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class AndonCallPostgresTests
{
    private static readonly DateTimeOffset RaisedAt = DateTimeOffset.Parse("2026-09-20T01:00:00Z");

    [MesRealPostgresFact]
    public async Task Migration_persists_complete_facts_and_replay_keeps_original_times_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var call = CreateCall();
        call.TryEscalate(RaisedAt.AddMinutes(5), TimeSpan.FromMinutes(5), "supervisor");
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(7));
        call.Close("worker", "close-1", RaisedAt.AddMinutes(9));
        db.Set<AndonCall>().Add(call);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await db.Set<AndonCall>().SingleAsync();
        saved.Claim("worker", "claim-1", RaisedAt.AddMinutes(20));
        saved.Close("worker", "close-1", RaisedAt.AddMinutes(21));
        Assert.False(saved.TryEscalate(RaisedAt.AddHours(1), TimeSpan.FromMinutes(1), "other"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await db.Set<AndonCall>().SingleAsync();
        Assert.Equal(("org-1", "env-1", "WO-1", "OP-1", "WC-1", "caller"),
            (reloaded.OrganizationId, reloaded.EnvironmentId, reloaded.WorkOrderId, reloaded.OperationTaskIdValue, reloaded.WorkCenterId, reloaded.CallerId));
        Assert.Equal(AndonCallCategory.Equipment, reloaded.Category);
        Assert.Equal(AndonCallStatus.Closed, reloaded.Status);
        Assert.Equal("raise-1", reloaded.RaiseIntentKey);
        Assert.Equal("claim-1", reloaded.ClaimIntentKey);
        Assert.Equal("close-1", reloaded.CloseIntentKey);
        Assert.Equal("worker", reloaded.ResponderId);
        Assert.Equal(RaisedAt, reloaded.RaisedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(7), reloaded.FirstRespondedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(9), reloaded.ClosedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(5), reloaded.EscalatedAtUtc);
        Assert.Equal("supervisor", reloaded.EscalationRecipientId);
        Assert.Equal(TimeSpan.FromMinutes(7), reloaded.ResponseDuration);
    }

    [MesRealPostgresFact]
    public async Task Stale_concurrent_claims_commit_exactly_one_responder_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        seed.Set<AndonCall>().Add(CreateCall());
        await seed.SaveChangesAsync();
        await using var first = CreateContext();
        await using var second = CreateContext();
        // 两个上下文都先读取同一未认领版本；即使数据库串行处理 UPDATE，也必须拒绝过期写入。
        var firstCall = await first.Set<AndonCall>().SingleAsync();
        var secondCall = await second.Set<AndonCall>().SingleAsync();
        firstCall.Claim("worker-1", "claim-1", RaisedAt.AddMinutes(1));
        secondCall.Claim("worker-2", "claim-2", RaisedAt.AddMinutes(2));
        var errors = await Task.WhenAll(
            Record.ExceptionAsync(() => first.SaveChangesAsync()),
            Record.ExceptionAsync(() => second.SaveChangesAsync()));
        Assert.Single(errors, error => error is null);
        Assert.IsType<DbUpdateConcurrencyException>(Assert.Single(errors, error => error is not null));
        await using var verification = CreateContext();
        var winner = await verification.Set<AndonCall>().SingleAsync();
        var firstWon = errors[0] is null;
        Assert.Equal(firstWon ? "worker-1" : "worker-2", winner.ResponderId);
        Assert.Equal(firstWon ? "claim-1" : "claim-2", winner.ClaimIntentKey);
        Assert.Equal(RaisedAt.AddMinutes(firstWon ? 1 : 2), winner.FirstRespondedAtUtc);
    }

    [MesRealPostgresFact]
    public async Task Raise_intent_is_unique_within_scope_and_does_not_cross_tenants_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.Set<AndonCall>().Add(CreateCall());
        db.Set<AndonCall>().Add(CreateCall("org-2"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Set<AndonCall>().Add(CreateCall());
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Set<AndonCall>().CountAsync());
        Assert.All(await db.Set<AndonCall>().ToListAsync(), call => Assert.Null(call.ResponseDuration));
    }

    private static AndonCall CreateCall(string organizationId = "org-1") =>
        AndonCall.Raise(organizationId, "env-1", "raise-1", AndonCallCategory.Equipment,
            "WO-1", "OP-1", "WC-1", "caller", RaisedAt);

    private static ApplicationDbContext CreateContext() => new(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
