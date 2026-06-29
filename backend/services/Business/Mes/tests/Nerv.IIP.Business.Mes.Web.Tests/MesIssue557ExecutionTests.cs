using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesIssue557ExecutionTests
{
    [Fact]
    public async Task Operation_start_rejects_later_sequence_before_previous_operations_complete()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_start_rejects_later_sequence_before_previous_operations_complete));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.Queued);
        await dbContext.SaveChangesAsync();

        var handler = new ChangeOperationTaskStateCommandHandler(dbContext, new NoRequirementsProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-20", "start", Utc("2026-06-29T09:00:00Z")),
            CancellationToken.None));

        Assert.Contains("前序工序", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operation_complete_rejects_later_sequence_before_previous_operations_complete()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_complete_rejects_later_sequence_before_previous_operations_complete));
        SeedReleasedWorkOrderWithTwoOperations(dbContext, secondStatus: OperationTaskLifecycleStatus.InProgress);
        await dbContext.SaveChangesAsync();

        var handler = new ChangeOperationTaskStateCommandHandler(dbContext, new NoRequirementsProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-20", "complete", Utc("2026-06-29T10:00:00Z")),
            CancellationToken.None));

        Assert.Contains("前序工序", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operation_pause_resume_completion_deducts_paused_time_from_labor_and_machine_hours()
    {
        await using var dbContext = CreateDbContext(nameof(Operation_pause_resume_completion_deducts_paused_time_from_labor_and_machine_hours));
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(4),
            null,
            null));
        await dbContext.SaveChangesAsync();
        var handler = new ChangeOperationTaskStateCommandHandler(dbContext, new NoRequirementsProvider());

        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "start", Utc("2026-06-29T08:00:00Z")), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "pause", Utc("2026-06-29T09:00:00Z")), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "resume", Utc("2026-06-29T10:00:00Z")), CancellationToken.None);
        await handler.Handle(new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "complete", Utc("2026-06-29T12:00:00Z")), CancellationToken.None);

        var task = await dbContext.OperationTasks.SingleAsync();
        Assert.Equal(TimeSpan.FromHours(1), ReadTimeSpan(task, "PausedDuration"));
        Assert.Equal(TimeSpan.FromHours(3), ReadTimeSpan(task, "LaborTime"));
        Assert.Equal(TimeSpan.FromHours(3), ReadTimeSpan(task, "MachineTime"));
    }

    [Fact]
    public async Task Scrap_report_requires_material_consumption_lots_to_drive_inventory_writeoff()
    {
        await using var dbContext = CreateDbContext(nameof(Scrap_report_requires_material_consumption_lots_to_drive_inventory_writeoff));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new RecordProductionReportCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                GoodQuantity: 0m,
                ScrapQuantity: 1m,
                CompletesOperation: false,
                ReportedAtUtc: Utc("2026-06-29T11:00:00Z")),
            CancellationToken.None));

        Assert.Contains("报废", exception.Message, StringComparison.Ordinal);
        Assert.Contains("耗料", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_operation_report_auto_generates_output_lot_and_persists_genealogy_breakpoint()
    {
        await using var dbContext = CreateDbContext(nameof(Output_operation_report_auto_generates_output_lot_and_persists_genealogy_breakpoint));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new RecordProductionReportCommandHandler(dbContext);

        var result = await handler.Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                GoodQuantity: 2m,
                ScrapQuantity: 0m,
                CompletesOperation: true,
                ReportedAtUtc: Utc("2026-06-29T11:00:00Z")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var report = await dbContext.ProductionReports.SingleAsync(x => x.Id == result.Id);
        Assert.False(string.IsNullOrWhiteSpace(report.ProducedLotNo));
        Assert.Contains(dbContext.Model.GetEntityTypes(), x => x.ClrType.Name == "OutputLotGenealogy");
    }

    [Fact]
    public async Task Finished_goods_receipt_requires_existing_output_lot_for_same_work_order()
    {
        await using var dbContext = CreateDbContext(nameof(Finished_goods_receipt_requires_existing_output_lot_for_same_work_order));
        SeedStartedOutputOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG",
                2m,
                "PCS",
                Utc("2026-06-29T12:00:00Z"),
                10m,
                ProducedLotNo: "LOT-NOT-REPORTED"),
            CancellationToken.None));

        Assert.Contains("产出批次", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Return_line_side_material_command_reduces_received_quantity_and_emits_inventory_reversal_intent()
    {
        await using var dbContext = CreateDbContext(nameof(Return_line_side_material_command_reduces_received_quantity_and_emits_inventory_reversal_intent));
        var materialRequest = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-001",
            "WO-001",
            "OP-10",
            "MAT-001",
            "PCS",
            5m,
            Utc("2026-06-29T08:00:00Z"));
        materialRequest.ConfirmLineSideReceipt(Utc("2026-06-29T08:30:00Z"), 5m, "LOT-MAT-001");
        materialRequest.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(materialRequest);
        await dbContext.SaveChangesAsync();

        var handler = new ReturnLineSideMaterialCommandHandler(dbContext);

        await handler.Handle(
            new ReturnLineSideMaterialCommand(
                "org-001",
                "env-dev",
                "MIR-001",
                Utc("2026-06-29T09:00:00Z"),
                2m),
            CancellationToken.None);

        Assert.Equal(3m, materialRequest.ReceivedQuantity);
        var eventNames = materialRequest.GetDomainEvents().Select(x => x.GetType().Name).ToArray();
        Assert.Contains("MaterialLineSideReturnRequestedDomainEvent", eventNames);
        Assert.Contains("MaterialReturnedToWarehouseDomainEvent", eventNames);
    }

    private static void SeedReleasedWorkOrderWithTwoOperations(ApplicationDbContext dbContext, OperationTaskLifecycleStatus secondStatus)
    {
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(1),
            null,
            null));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-20",
            secondStatus,
            20,
            "WC-20",
            [],
            Utc("2026-06-29T09:00:00Z"),
            TimeSpan.FromHours(1),
            secondStatus == OperationTaskLifecycleStatus.InProgress ? Utc("2026-06-29T09:00:00Z") : null,
            null));
    }

    private static void SeedStartedOutputOperation(ApplicationDbContext dbContext)
    {
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, Utc("2026-06-30T08:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(Utc("2026-06-29T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-10",
            [],
            Utc("2026-06-29T08:00:00Z"),
            TimeSpan.FromHours(1),
            Utc("2026-06-29T08:00:00Z"),
            null));
    }

    private static TimeSpan ReadTimeSpan(OperationTask task, string propertyName)
    {
        var property = typeof(OperationTask).GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<TimeSpan>(property.GetValue(task));
    }

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value);

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoRequirementsProvider : IMesMaterialRequirementSnapshotProvider
    {
        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test"));
        }
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
