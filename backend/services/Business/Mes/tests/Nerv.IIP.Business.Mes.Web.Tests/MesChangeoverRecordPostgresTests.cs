using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ChangeoverRecordAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesChangeoverRecordTests
{
    [Fact]
    public void Start_and_complete_preserve_operator_tooling_check_and_actual_window()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-30T01:00:00Z");
        var completedAtUtc = startedAtUtc.AddMinutes(35);

        var record = ChangeoverRecord.Start(
            "org-001",
            "env-dev",
            "CO-0001",
            "WC-01",
            "DEV-01",
            "operator-01",
            ChangeoverToolingCheckResult.Passed,
            startedAtUtc);
        record.Complete(completedAtUtc);

        Assert.Equal("operator-01", record.OperatorId);
        Assert.Equal(ChangeoverToolingCheckResult.Passed, record.ToolingCheckResult);
        Assert.Equal(startedAtUtc, record.StartedAtUtc);
        Assert.Equal(completedAtUtc, record.CompletedAtUtc);
    }

    [Fact]
    public void Complete_rejects_time_before_start_and_a_second_completion()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-08-30T01:00:00Z");
        var record = ChangeoverRecord.Start(
            "org-001",
            "env-dev",
            "CO-0002",
            "WC-01",
            "DEV-01",
            "operator-01",
            ChangeoverToolingCheckResult.Failed,
            startedAtUtc);

        Assert.Throws<KnownException>(() => record.Complete(startedAtUtc.AddMinutes(-1)));

        record.Complete(startedAtUtc.AddMinutes(10));
        Assert.Throws<KnownException>(() => record.Complete(startedAtUtc.AddMinutes(20)));
    }
}

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesChangeoverRecordPostgresTests
{
    [MesRealPostgresFact]
    public async Task Start_fields_and_completion_are_persisted_by_the_mes_migration()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), NoopMediator.Instance);
        await db.Database.MigrateAsync();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-30T01:00:00Z");
        db.ChangeoverRecords.Add(ChangeoverRecord.Start(
            "org-001",
            "env-dev",
            "CO-POSTGRES-001",
            "WC-01",
            "DEV-01",
            "operator-01",
            ChangeoverToolingCheckResult.Passed,
            startedAtUtc));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var completedAtUtc = startedAtUtc.AddMinutes(35);
        await new CompleteChangeoverCommandHandler(db).Handle(new(
            "org-001", "env-dev", "CO-POSTGRES-001", completedAtUtc), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var persisted = await db.ChangeoverRecords.SingleAsync();
        Assert.Equal("operator-01", persisted.OperatorId);
        Assert.Equal(ChangeoverToolingCheckResult.Passed, persisted.ToolingCheckResult);
        Assert.Equal(completedAtUtc, persisted.CompletedAtUtc);
    }

    private sealed class NoopMediator : IMediator
    {
        public static NoopMediator Instance { get; } = new();

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
