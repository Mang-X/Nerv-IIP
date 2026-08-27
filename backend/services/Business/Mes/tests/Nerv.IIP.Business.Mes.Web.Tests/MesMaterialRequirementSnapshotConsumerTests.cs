using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// Contract: DomainInvariant + Regression. Authority: Issue #2234 and the reviewer-confirmed PR #2284
// convergence plan: a capture is a complete work-order snapshot, and Local overrides persisted by Id.
/// <summary>
/// Material requirement snapshots are complete work-order captures. Every runtime consumer must first
/// choose the latest capture, then apply operation scope and aggregate rows; tracked duplicates must not
/// turn one persisted requirement into two requirements.
/// </summary>
public sealed class MesMaterialRequirementSnapshotConsumerTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private const string WorkOrderId = "WO-SNAPSHOT";
    private static readonly DateTimeOffset Earlier = DateTimeOffset.Parse("2026-08-27T08:00:00Z");
    private static readonly DateTimeOffset Latest = DateTimeOffset.Parse("2026-08-27T09:00:00Z");

    [Theory]
    [InlineData("OP-10", 9)]
    [InlineData(null, 109)]
    public async Task Default_issue_quantity_sums_latest_capture_after_operation_scope(
        string? operationTaskId,
        decimal expectedQuantity)
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(CreateReleasedWorkOrder());
        dbContext.MaterialRequirements.AddRange(
            Requirement(null, "MAT-A", "LOT-WO", 2m, Latest),
            Requirement("OP-10", "MAT-A", "LOT-10-A", 3m, Latest),
            Requirement("OP-10", "MAT-A", "LOT-10-B", 4m, Latest),
            Requirement("OP-20", "MAT-A", "LOT-20", 100m, Latest),
            Requirement("OP-10", "MAT-B", null, 50m, Latest));
        await dbContext.SaveChangesAsync();

        var accepted = await new CreateMaterialIssueRequestCommandHandler(dbContext).Handle(
            new CreateMaterialIssueRequestCommand(
                OrganizationId,
                EnvironmentId,
                WorkOrderId,
                operationTaskId,
                "MAT-A",
                "PCS",
                null,
                Latest.AddMinutes(1)),
            CancellationToken.None);

        var issue = Assert.Single(dbContext.MaterialIssueRequests.Local, x => x.RequestNo == accepted.ReferenceId);
        Assert.Equal(expectedQuantity, issue.RequestedQuantity);
    }

    [Fact]
    public async Task Default_issue_quantity_does_not_revive_material_deleted_from_latest_capture()
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(CreateReleasedWorkOrder());
        dbContext.MaterialRequirements.AddRange(
            Requirement("OP-10", "MAT-A", null, 5m, Earlier),
            Requirement("OP-10", "MAT-B", null, 7m, Latest));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateMaterialIssueRequestCommandHandler(dbContext).Handle(
                new CreateMaterialIssueRequestCommand(
                    OrganizationId,
                    EnvironmentId,
                    WorkOrderId,
                    "OP-10",
                    "MAT-A",
                    "PCS",
                    null,
                    Latest.AddMinutes(1)),
                CancellationToken.None));

        Assert.Contains("数量必须大于 0", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dbContext.MaterialIssueRequests.Local);
    }

    [Fact]
    public async Task Overview_does_not_count_material_deleted_from_latest_capture_as_shortage()
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(CreateReleasedWorkOrder());
        dbContext.MaterialRequirements.AddRange(
            Requirement("OP-10", "MAT-A", null, 5m, Earlier),
            Requirement("OP-20", "MAT-B", null, 7m, Latest, availableQuantity: 7m));
        await dbContext.SaveChangesAsync();

        var response = await new GetMesOverviewQueryHandler(dbContext).Handle(
            new GetMesOverviewQuery(OrganizationId, EnvironmentId),
            CancellationToken.None);

        Assert.DoesNotContain(response.Blockers, x => x.Code == "MATERIAL_SHORTAGE");
    }

    [Fact]
    public async Task Operation_readiness_does_not_revive_requirement_deleted_from_latest_capture()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateReleasedWorkOrder();
        workOrder.RecordMaterialRequirementSnapshot(WorkOrder.MaterialRequirementSnapshotCapturedStatus, Latest);
        var task = CreateQueuedTask("OP-10", 10);
        dbContext.AddRange(workOrder, task);
        dbContext.MaterialRequirements.AddRange(
            Requirement("OP-10", "MAT-A", null, 5m, Earlier),
            Requirement("OP-20", "MAT-B", null, 7m, Latest, availableQuantity: 7m));
        await dbContext.SaveChangesAsync();

        var readiness = await new MesOperationTaskActionReadinessEvaluator(dbContext).EvaluateAsync(
            task,
            Latest.AddMinutes(1),
            CancellationToken.None);

        Assert.Empty(readiness.BlockReasons);
        Assert.Equal(["start"], readiness.AllowedActions);
    }

    [Fact]
    public async Task Operation_readiness_deduplicates_tracked_copy_of_same_requirement_id()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = CreateReleasedWorkOrder();
        workOrder.RecordMaterialRequirementSnapshot(WorkOrder.MaterialRequirementSnapshotCapturedStatus, Latest);
        var task = CreateQueuedTask("OP-10", 10);
        var requirement = Requirement("OP-10", "MAT-A", null, 10m, Latest);
        var receipt = ReceivedIssue("OP-10", "MAT-A", 5m);
        dbContext.AddRange(workOrder, task, requirement, receipt);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(requirement).Property(x => x.RequiredQuantity).CurrentValue = 5m;

        var readiness = await new MesOperationTaskActionReadinessEvaluator(dbContext).EvaluateAsync(
            task,
            Latest.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(["start"], readiness.AllowedActions);
        Assert.Empty(readiness.BlockReasons);
    }

    [Fact]
    public async Task Shortage_guard_does_not_revive_operation_row_deleted_from_latest_capture()
    {
        await using var dbContext = CreateDbContext();
        dbContext.WorkOrders.Add(CreateReleasedWorkOrder());
        dbContext.MaterialRequirements.AddRange(
            Requirement("OP-10", "MAT-A", null, 5m, Earlier),
            Requirement("OP-20", "MAT-B", null, 7m, Latest));
        await dbContext.SaveChangesAsync();

        var reasons = await MaterialReadinessGuards.GetShortageReasonsAsync(
            dbContext,
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            "OP-10",
            CancellationToken.None);

        Assert.Empty(reasons);
    }

    [Fact]
    public async Task Shortage_guard_deduplicates_tracked_copy_of_same_requirement_id()
    {
        await using var dbContext = CreateDbContext();
        var requirement = Requirement("OP-10", "MAT-A", null, 10m, Latest);
        var receipt = ReceivedIssue("OP-10", "MAT-A", 5m);
        dbContext.AddRange(CreateReleasedWorkOrder(), requirement, receipt);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(requirement).Property(x => x.RequiredQuantity).CurrentValue = 5m;

        var reasons = await MaterialReadinessGuards.GetShortageReasonsAsync(
            dbContext,
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            "OP-10",
            CancellationToken.None);

        Assert.Empty(reasons);
    }

    private static WorkOrder CreateReleasedWorkOrder()
    {
        var workOrder = WorkOrder.Create(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            "SKU-FG-1000",
            "PV-1",
            10m,
            10,
            Latest.AddDays(1),
            "PCS");
        workOrder.MarkReleased();
        return workOrder;
    }

    private static OperationTask CreateQueuedTask(string operationTaskId, int sequence) =>
        OperationTask.Create(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            operationTaskId,
            OperationTaskLifecycleStatus.Queued,
            sequence,
            "WC-ASSEMBLY",
            [],
            Latest,
            TimeSpan.FromHours(1),
            null,
            null);

    private static MaterialRequirement Requirement(
        string? operationTaskId,
        string materialId,
        string? lotId,
        decimal requiredQuantity,
        DateTimeOffset capturedAtUtc,
        decimal availableQuantity = 0m) =>
        MaterialRequirement.Capture(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            operationTaskId,
            materialId,
            lotId,
            requiredQuantity,
            availableQuantity,
            0m,
            "test",
            $"snapshot-{capturedAtUtc:yyyyMMddHHmm}-{operationTaskId ?? "work-order"}-{materialId}-{lotId}",
            capturedAtUtc,
            []);

    private static MaterialIssueRequest ReceivedIssue(
        string? operationTaskId,
        string materialId,
        decimal quantity)
    {
        var issue = MaterialIssueRequest.Create(
            OrganizationId,
            EnvironmentId,
            $"MIR-{Guid.CreateVersion7():N}",
            WorkOrderId,
            operationTaskId,
            materialId,
            "PCS",
            quantity,
            Latest);
        issue.ConfirmAndPostLineSideReceipt(
            new MaterialTransferLocations(
                "SITE-01",
                "WH-01",
                "SITE-01",
                "LINE-01",
                [new MaterialTransferAllocation("SITE-01", "WH-01", null, quantity)]),
            Latest,
            quantity);
        return issue;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-material-snapshot-consumers-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
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
