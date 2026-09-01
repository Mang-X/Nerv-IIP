using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesCollaborationPostgresTests
{
    [MesRealPostgresFact]
    public async Task Reportable_scope_matches_a_registered_participant_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), NoopMediator.Instance);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-COLLAB", "SKU-001", "PV-001", 10m, 1, now.AddDays(1), "PCS");
        workOrder.MarkReleased();
        var operation = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-COLLAB",
            "OP-COLLAB",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-01",
            [],
            now,
            TimeSpan.FromHours(1),
            null,
            null);
        operation.Assign("lead-worker", null, null, now, assignedUserName: "Lead Worker");
        operation.Start(now.AddMinutes(1));
        db.WorkOrders.Add(workOrder);
        db.OperationTasks.Add(operation);
        db.OperationTaskParticipants.Add(OperationTaskParticipant.Register(
            "org-001", "env-dev", "OP-COLLAB", "participant-worker", "协作员工", 40m));
        await db.SaveChangesAsync();

        var result = await new ListReportableOperationTasksQueryHandler(db).Handle(
            new ListReportableOperationTasksQuery(
                "org-001",
                "env-dev",
                Status: nameof(OperationTaskLifecycleStatus.InProgress),
                AssignedUserIds: "participant-worker"),
            CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal("OP-COLLAB", Assert.Single(result.Items).OperationTaskId);
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
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
