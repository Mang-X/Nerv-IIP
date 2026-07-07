using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

public sealed record WmsInboundLineInput(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string StagingLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);

public sealed record WmsOutboundLineInput(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal RequestedQuantity,
    string PickLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record CreateInboundOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string InboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SiteCode,
    IReadOnlyCollection<WmsInboundLineInput> Lines) : ICommand<InboundOrderId>;

public sealed class CreateInboundOrderCommandValidator : AbstractValidator<CreateInboundOrderCommand>
{
    public CreateInboundOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InboundOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class CreateInboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateInboundOrderCommand, InboundOrderId>
{
    public async Task<InboundOrderId> Handle(CreateInboundOrderCommand request, CancellationToken cancellationToken)
    {
        var order = InboundOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.InboundOrderNo,
            request.SourceDocumentType,
            request.SourceDocumentId,
            request.SiteCode,
            request.Lines.Select(x => new InboundOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.ReceivedQuantity, x.StagingLocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId, x.ProductionDate, x.ExpiryDate)));
        dbContext.InboundOrders.Add(order);
        await Task.CompletedTask;
        return order.Id;
    }
}

public sealed record CreatePutawayTaskCommand(InboundOrderId InboundOrderId, string TaskNo, string LineNo, string FromLocationCode, string ToLocationCode, decimal Quantity) : ICommand<WarehouseTaskId>;

public sealed class CreatePutawayTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreatePutawayTaskCommand, WarehouseTaskId>
{
    public async Task<WarehouseTaskId> Handle(CreatePutawayTaskCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundOrderId, cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        var task = inbound.CreatePutawayTask(request.TaskNo, request.LineNo, request.FromLocationCode, request.ToLocationCode, request.Quantity);
        dbContext.WarehouseTasks.Add(task);
        return task.Id;
    }
}

public sealed record CompleteInboundOrderCommand(InboundOrderId InboundOrderId, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed record CompleteWmsMovementResult(InventoryMovementRequestId? RequestId, string? InventoryMovementId);

public sealed class CompleteInboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteInboundOrderCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteInboundOrderCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundOrderId, cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        var movementRequests = inbound.Complete(request.IdempotencyKey);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        return new CompleteWmsMovementResult(movementRequests.First().Id, null);
    }
}

public sealed record RetryInboundInventoryPostingCommand(InboundOrderId InboundOrderId, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class RetryInboundInventoryPostingCommandValidator : AbstractValidator<RetryInboundInventoryPostingCommand>
{
    public RetryInboundInventoryPostingCommandValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public sealed class RetryInboundInventoryPostingCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RetryInboundInventoryPostingCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(RetryInboundInventoryPostingCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundOrderId, cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        var movementRequests = inbound.RetryInventoryPosting(request.IdempotencyKey);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        return new CompleteWmsMovementResult(movementRequests.First().Id, null);
    }
}

public sealed record CreateOutboundOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string OutboundOrderNo,
    string SourceDocumentType,
    string SourceDocumentId,
    string SiteCode,
    IReadOnlyCollection<WmsOutboundLineInput> Lines) : ICommand<OutboundOrderId>;

public sealed class CreateOutboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOutboundOrderCommand, OutboundOrderId>
{
    public async Task<OutboundOrderId> Handle(CreateOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        var existingOrder = await dbContext.OutboundOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.OutboundOrderNo == request.OutboundOrderNo,
            cancellationToken);
        if (existingOrder is not null)
        {
            return existingOrder.Id;
        }

        var order = OutboundOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.OutboundOrderNo,
            request.SourceDocumentType,
            request.SourceDocumentId,
            request.SiteCode,
            request.Lines.Select(x => new OutboundOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.RequestedQuantity, x.PickLocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId)));
        dbContext.OutboundOrders.Add(order);
        await Task.CompletedTask;
        return order.Id;
    }
}

public sealed record CreatePickingTaskCommand(OutboundOrderId OutboundOrderId, string TaskNo, string LineNo, string FromLocationCode, string ToLocationCode, decimal Quantity) : ICommand<WarehouseTaskId>;

public sealed class CreatePickingTaskCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CreatePickingTaskCommand, WarehouseTaskId>
{
    public async Task<WarehouseTaskId> Handle(CreatePickingTaskCommand request, CancellationToken cancellationToken)
    {
        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new KnownException($"Outbound order was not found: {request.OutboundOrderId}");
        var line = outbound.Lines.SingleOrDefault(x => x.LineNo == request.LineNo)
            ?? throw new KnownException($"Outbound line was not found: {request.LineNo}");
        try
        {
            outbound.EnsureCanCreatePickingTask(line.LineNo, request.Quantity);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        // Remote Inventory reservation and local WMS task persistence are not atomic; the stable
        // line-level idempotency key lets command retries recover the same reservation.
        var reservation = line.InventoryReservationId is null && inventoryReservationClient is not null
            ? await ReserveInventoryForPickingAsync(inventoryReservationClient, outbound, line, request.FromLocationCode, request.Quantity, cancellationToken)
            : null;
        var inventoryReservationId = line.InventoryReservationId ?? reservation?.ReservationId;
        var task = outbound.CreatePickingTask(
            request.TaskNo,
            request.LineNo,
            request.FromLocationCode,
            request.ToLocationCode,
            request.Quantity,
            inventoryReservationId,
            reservation?.LocationCode,
            reservation?.LotNo,
            reservation?.SerialNo);
        dbContext.WarehouseTasks.Add(task);
        return task.Id;
    }

    private static async Task<PickingReservationResult> ReserveInventoryForPickingAsync(
        IWmsInventoryReservationClient inventoryReservationClient,
        OutboundOrder outbound,
        OutboundOrderLine line,
        string fromLocationCode,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = WmsInventoryReservationIdempotencyKeys.ForPickingTask(outbound, line.LineNo);
        if (string.IsNullOrWhiteSpace(line.LotNo))
        {
            var fefo = await inventoryReservationClient.ReserveFefoAsync(
                new WmsInventoryFefoReservationRequest(
                    outbound.OrganizationId,
                    outbound.EnvironmentId,
                    "wms",
                    outbound.OutboundOrderNo,
                    line.LineNo,
                    idempotencyKey,
                    line.SkuCode,
                    line.UomCode,
                    outbound.SiteCode,
                    line.QualityStatus,
                    line.OwnerType,
                    line.OwnerId,
                    quantity,
                    fromLocationCode),
                cancellationToken);
            if (fefo.Allocations.Count != 1)
            {
                await ReleaseRejectedFefoAllocationsAsync(inventoryReservationClient, fefo, cancellationToken);
                throw new KnownException("Inventory FEFO reservation split the picking line; WMS split-pick execution is outside the current issue scope.");
            }

            var allocation = fefo.Allocations.Single();
            if (allocation.ReservedQuantity != quantity)
            {
                await ReleaseRejectedFefoAllocationsAsync(inventoryReservationClient, fefo, cancellationToken);
                throw new KnownException("Inventory FEFO reservation split the picking line; WMS split-pick execution is outside the current issue scope.");
            }

            return new PickingReservationResult(allocation.ReservationId, allocation.LocationCode, allocation.LotNo, allocation.SerialNo);
        }

        var reservation = await inventoryReservationClient.ReserveAsync(
            new WmsInventoryReservationRequest(
                outbound.OrganizationId,
                outbound.EnvironmentId,
                "wms",
                outbound.OutboundOrderNo,
                line.LineNo,
                idempotencyKey,
                line.SkuCode,
                line.UomCode,
                outbound.SiteCode,
                fromLocationCode,
                line.LotNo,
                line.SerialNo,
                line.QualityStatus,
                line.OwnerType,
                line.OwnerId,
                quantity),
            cancellationToken);
        return new PickingReservationResult(reservation.ReservationId, fromLocationCode, reservation.LotNo ?? line.LotNo, line.SerialNo);
    }

    private static async Task ReleaseRejectedFefoAllocationsAsync(
        IWmsInventoryReservationClient inventoryReservationClient,
        WmsInventoryFefoReservationResult fefo,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in fefo.Allocations)
        {
            await inventoryReservationClient.ReleaseAsync(
                new WmsInventoryReservationReleaseRequest(allocation.ReservationId, allocation.ReservedQuantity),
                cancellationToken);
        }
    }
}

public sealed record PickingReservationResult(string ReservationId, string LocationCode, string? LotNo, string? SerialNo);

public sealed record RecordWarehouseTaskProgressCommand(WarehouseTaskId WarehouseTaskId, decimal ExecutedQuantity) : ICommand;

public sealed class RecordWarehouseTaskProgressCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordWarehouseTaskProgressCommand>
{
    public async Task Handle(RecordWarehouseTaskProgressCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.WarehouseTasks.SingleOrDefaultAsync(x => x.Id == request.WarehouseTaskId, cancellationToken)
            ?? throw new KnownException($"Warehouse task was not found: {request.WarehouseTaskId}");
        task.RecordProgress(request.ExecutedQuantity);
    }
}

public sealed record CompleteWarehouseTaskCommand(WarehouseTaskId WarehouseTaskId) : ICommand;

public sealed class CompleteWarehouseTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteWarehouseTaskCommand>
{
    public async Task Handle(CompleteWarehouseTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.WarehouseTasks.SingleOrDefaultAsync(x => x.Id == request.WarehouseTaskId, cancellationToken)
            ?? throw new KnownException($"Warehouse task was not found: {request.WarehouseTaskId}");
        task.RecordProgress(task.PlannedQuantity);
    }
}

public sealed record CompleteOutboundOrderCommand(OutboundOrderId OutboundOrderId, string PackReviewNo, bool Passed, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class CompleteOutboundOrderCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CompleteOutboundOrderCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new KnownException($"Outbound order was not found: {request.OutboundOrderId}");
        var executedQuantitiesByLine = await GetExecutedPickingQuantitiesAsync(outbound, cancellationToken);
        EnsureInventoryClientAvailableForShortPickRelease(outbound, executedQuantitiesByLine);
        var movementRequests = outbound.CompletePackReview(request.PackReviewNo, request.Passed, request.IdempotencyKey, executedQuantitiesByLine);
        await ReleaseShortPickedReservationBalancesAsync(outbound, cancellationToken);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        return new CompleteWmsMovementResult(movementRequests.First().Id, null);
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> GetExecutedPickingQuantitiesAsync(
        OutboundOrder outbound,
        CancellationToken cancellationToken)
    {
        var taskExecutions = await dbContext.WarehouseTasks
            .Where(x => x.OrganizationId == outbound.OrganizationId
                && x.EnvironmentId == outbound.EnvironmentId
                && x.TaskType == WarehouseTaskType.Picking
                && x.SourceOrderNo == outbound.OutboundOrderNo)
            .GroupBy(x => x.SourceOrderLineNo)
            .Select(x => new { LineNo = x.Key, ExecutedQuantity = x.Sum(task => task.ExecutedQuantity) })
            .ToArrayAsync(cancellationToken);

        return taskExecutions.Length == 0
            ? null
            : taskExecutions.ToDictionary(x => x.LineNo, x => x.ExecutedQuantity, StringComparer.Ordinal);
    }

    private void EnsureInventoryClientAvailableForShortPickRelease(
        OutboundOrder outbound,
        IReadOnlyDictionary<string, decimal>? executedQuantitiesByLine)
    {
        if (inventoryReservationClient is not null || executedQuantitiesByLine is null)
        {
            return;
        }

        var requiresRelease = outbound.Lines.Any(line =>
            line.InventoryReservationId is not null
            && executedQuantitiesByLine.TryGetValue(line.LineNo, out var executedQuantity)
            && executedQuantity >= 0
            && Math.Min(executedQuantity, line.RequestedQuantity) < line.RequestedQuantity);
        if (requiresRelease)
        {
            throw new KnownException("Inventory reservation client is required to release short-picked reserved stock before completing outbound order.");
        }
    }

    private async Task ReleaseShortPickedReservationBalancesAsync(
        OutboundOrder outbound,
        CancellationToken cancellationToken)
    {
        foreach (var line in outbound.Lines.Where(x => x.InventoryReservationId is not null && x.BackorderQuantity > 0))
        {
            if (inventoryReservationClient is null)
            {
                throw new KnownException("Inventory reservation client is required to release short-picked reserved stock.");
            }

            await inventoryReservationClient.ReleaseAsync(
                new WmsInventoryReservationReleaseRequest(line.InventoryReservationId!, line.BackorderQuantity),
                cancellationToken);
        }
    }
}

public sealed record CancelOutboundOrderCommand(OutboundOrderId OutboundOrderId, string Reason) : ICommand;

public sealed class CancelOutboundOrderCommandValidator : AbstractValidator<CancelOutboundOrderCommand>
{
    public CancelOutboundOrderCommandValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CancelOutboundOrderCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CancelOutboundOrderCommand>
{
    public async Task Handle(CancelOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        _ = WmsText.Required(request.Reason, nameof(request.Reason));
        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new KnownException($"Outbound order was not found: {request.OutboundOrderId}");
        outbound.EnsureCanCancel();
        var openPickingTasks = await dbContext.WarehouseTasks
            .Where(x => x.OrganizationId == outbound.OrganizationId
                && x.EnvironmentId == outbound.EnvironmentId
                && x.TaskType == WarehouseTaskType.Picking
                && x.SourceOrderNo == outbound.OutboundOrderNo
                && x.Status == WarehouseTaskStatus.Open)
            .ToArrayAsync(cancellationToken);
        var openPickingTaskIds = openPickingTasks.Select(x => x.Id).ToArray();
        var cancellableWcsTasks = await dbContext.WcsTasks
            .Where(x => openPickingTaskIds.Contains(x.WarehouseTaskId) && x.Status != WcsTaskStatus.Completed)
            .ToArrayAsync(cancellationToken);
        foreach (var line in outbound.Lines.Where(x => x.InventoryReservationId is not null))
        {
            if (inventoryReservationClient is null)
            {
                throw new KnownException("Inventory reservation client is required to cancel an outbound order with reserved stock.");
            }

            await inventoryReservationClient.ReleaseAsync(
                new WmsInventoryReservationReleaseRequest(line.InventoryReservationId!, line.RequestedQuantity),
                cancellationToken);
        }

        outbound.Cancel(request.Reason);
        foreach (var task in openPickingTasks)
        {
            task.Cancel();
        }

        foreach (var task in cancellableWcsTasks)
        {
            task.Cancel();
        }
    }
}

public sealed record RetryOutboundInventoryPostingCommand(OutboundOrderId OutboundOrderId, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class RetryOutboundInventoryPostingCommandValidator : AbstractValidator<RetryOutboundInventoryPostingCommand>
{
    public RetryOutboundInventoryPostingCommandValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public sealed class RetryOutboundInventoryPostingCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<RetryOutboundInventoryPostingCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(RetryOutboundInventoryPostingCommand request, CancellationToken cancellationToken)
    {
        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new KnownException($"Outbound order was not found: {request.OutboundOrderId}");
        var failedRequests = await dbContext.InventoryMovementRequests
            .Where(x => x.OrganizationId == outbound.OrganizationId
                && x.EnvironmentId == outbound.EnvironmentId
                && x.MovementType == "outbound"
                && x.SourceDocumentId == outbound.OutboundOrderNo
                && x.Status == InventoryMovementRequestStatus.Failed)
            .ToArrayAsync(cancellationToken);
        var failedLineNos = failedRequests
            .Select(x => x.SourceDocumentLineId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        outbound.EnsureCanRetryInventoryPosting(failedLineNos);
        if (inventoryReservationClient is null)
        {
            throw new KnownException("Inventory reservation client is required to retry outbound Inventory posting.");
        }

        var reservationIds = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var line in outbound.Lines.Where(x => failedLineNos.Contains(x.LineNo, StringComparer.Ordinal)).OrderBy(x => x.LineNo, StringComparer.Ordinal))
        {
            var reservationId = (await inventoryReservationClient.ReserveAsync(
                new WmsInventoryReservationRequest(
                    outbound.OrganizationId,
                    outbound.EnvironmentId,
                    "wms",
                    outbound.OutboundOrderNo,
                    line.LineNo,
                    WmsInventoryReservationIdempotencyKeys.ForOutboundRetry(outbound, line.LineNo, request.IdempotencyKey),
                    line.SkuCode,
                    line.UomCode,
                    outbound.SiteCode,
                    line.PickLocationCode,
                    line.LotNo,
                    line.SerialNo,
                    line.QualityStatus,
                    line.OwnerType,
                    line.OwnerId,
                    line.RequestedQuantity),
                cancellationToken)).ReservationId;

            reservationIds[line.LineNo] = reservationId;
        }

        var movementRequests = outbound.RetryInventoryPosting(request.IdempotencyKey, reservationIds);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        return new CompleteWmsMovementResult(movementRequests.First().Id, null);
    }
}

public sealed record CreateCountExecutionCommand(string OrganizationId, string EnvironmentId, string CountNo, string SkuCode, string UomCode, string SiteCode, string LocationCode, decimal ExpectedQuantity) : ICommand<CountExecutionId>;

public sealed class CreateCountExecutionCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CreateCountExecutionCommand, CountExecutionId>
{
    public async Task<CountExecutionId> Handle(CreateCountExecutionCommand request, CancellationToken cancellationToken)
    {
        var count = CountExecution.Create(request.OrganizationId, request.EnvironmentId, request.CountNo, request.SkuCode, request.UomCode, request.SiteCode, request.LocationCode, request.ExpectedQuantity);
        if (inventoryReservationClient is not null)
        {
            var countTask = await inventoryReservationClient.CreateCountTaskAsync(ToInventoryCountTaskRequest(count), cancellationToken);
            count.MarkInventoryCountTaskCreated(countTask.CountTaskId);
        }

        dbContext.CountExecutions.Add(count);
        await Task.CompletedTask;
        return count.Id;
    }

    internal static WmsInventoryCountTaskRequest ToInventoryCountTaskRequest(CountExecution count)
    {
        return new WmsInventoryCountTaskRequest(
            count.OrganizationId,
            count.EnvironmentId,
            count.CountNo,
            count.SkuCode,
            count.UomCode,
            count.SiteCode,
            count.LocationCode,
            null,
            null,
            "qualified",
            "company",
            null,
            WmsInventoryReservationIdempotencyKeys.ForCountExecution(count));
    }
}

public sealed record CompleteCountExecutionCommand(CountExecutionId CountExecutionId, decimal CountedQuantity, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class CompleteCountExecutionCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CompleteCountExecutionCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteCountExecutionCommand request, CancellationToken cancellationToken)
    {
        var count = await dbContext.CountExecutions.SingleOrDefaultAsync(x => x.Id == request.CountExecutionId, cancellationToken)
            ?? throw new KnownException($"Count execution was not found: {request.CountExecutionId}");
        if (inventoryReservationClient is not null)
        {
            if (count.InventoryCountTaskId is null)
            {
                var countTask = await inventoryReservationClient.CreateCountTaskAsync(
                    CreateCountExecutionCommandHandler.ToInventoryCountTaskRequest(count),
                    cancellationToken);
                count.MarkInventoryCountTaskCreated(countTask.CountTaskId);
            }

            var adjustment = await inventoryReservationClient.ConfirmCountAdjustmentAsync(
                new WmsInventoryCountAdjustmentRequest(count.InventoryCountTaskId!, request.CountedQuantity, request.IdempotencyKey),
                cancellationToken);
            count.Complete(request.CountedQuantity);
            return new CompleteWmsMovementResult(null, adjustment.MovementId);
        }

        count.Complete(request.CountedQuantity);
        var varianceQuantity = count.VarianceQuantity
            ?? throw new KnownException("Count execution variance was not calculated.");
        var movementRequest = InventoryMovementRequest.Create(
            count.OrganizationId,
            count.EnvironmentId,
            "count-adjustment",
            count.CountNo,
            null,
            request.IdempotencyKey,
            count.SkuCode,
            count.UomCode,
            count.SiteCode,
            count.LocationCode,
            null,
            null,
            "qualified",
            "company",
            null,
            varianceQuantity);
        dbContext.InventoryMovementRequests.Add(movementRequest);
        return new CompleteWmsMovementResult(movementRequest.Id, null);
    }
}

public sealed record MarkInventoryMovementRequestPostedCommand(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string InventoryMovementId) : ICommand;

public sealed class MarkInventoryMovementRequestPostedCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<MarkInventoryMovementRequestPostedCommand>
{
    public async Task Handle(MarkInventoryMovementRequestPostedCommand request, CancellationToken cancellationToken)
    {
        var movementRequest = await dbContext.InventoryMovementRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.MovementType == request.MovementType
                && x.SourceDocumentId == request.SourceDocumentId
                && x.SourceDocumentLineId == request.SourceDocumentLineId
                && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (movementRequest is null)
        {
            return;
        }

        movementRequest.MarkPosted(request.InventoryMovementId);
    }
}

public sealed record MarkInventoryMovementRequestFailedCommand(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string FailureCode,
    string FailureMessage) : ICommand;

public sealed class MarkInventoryMovementRequestFailedCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<MarkInventoryMovementRequestFailedCommand>
{
    public async Task Handle(MarkInventoryMovementRequestFailedCommand request, CancellationToken cancellationToken)
    {
        var movementRequest = await dbContext.InventoryMovementRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.MovementType == request.MovementType
                && x.SourceDocumentId == request.SourceDocumentId
                && x.SourceDocumentLineId == request.SourceDocumentLineId
                && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (movementRequest is null)
        {
            return;
        }

        if (request.MovementType == "outbound" && movementRequest.InventoryReservationId is not null && inventoryReservationClient is not null)
        {
            await inventoryReservationClient.ReleaseAsync(
                new WmsInventoryReservationReleaseRequest(movementRequest.InventoryReservationId, Math.Abs(movementRequest.Quantity)),
                cancellationToken);
        }
        else if (request.MovementType == "outbound" && movementRequest.InventoryReservationId is not null)
        {
            throw new KnownException("Inventory reservation client is required to release failed outbound reserved stock.");
        }

        movementRequest.MarkFailed(request.FailureCode, request.FailureMessage);
        if (request.MovementType == "inbound")
        {
            var inbound = await dbContext.InboundOrders.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.InboundOrderNo == request.SourceDocumentId,
                cancellationToken);
            inbound?.MarkInventoryPostingFailed();
        }
        else if (request.MovementType == "outbound")
        {
            var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.OutboundOrderNo == request.SourceDocumentId,
                cancellationToken);
            if (movementRequest.InventoryReservationId is not null)
            {
                outbound?.MarkInventoryReservationReleased(movementRequest.InventoryReservationId);
            }

            outbound?.MarkInventoryPostingFailed();
        }
    }
}

internal static class WmsInventoryReservationIdempotencyKeys
{
    public static string ForPickingTask(OutboundOrder outbound, string lineNo)
    {
        var raw = $"{outbound.OrganizationId}:{outbound.EnvironmentId}:{outbound.OutboundOrderNo}:{lineNo}";
        return $"wms-pick-res:{StableHash(raw)}";
    }

    public static string ForOutboundRetry(OutboundOrder outbound, string lineNo, string retryIdempotencyKey)
    {
        var raw = $"{outbound.OrganizationId}:{outbound.EnvironmentId}:{outbound.OutboundOrderNo}:{lineNo}:{retryIdempotencyKey}";
        return $"wms-retry-res:{StableHash(raw)}";
    }

    public static string ForCountExecution(CountExecution count)
    {
        var raw = $"{count.OrganizationId}:{count.EnvironmentId}:{count.CountNo}";
        return $"wms-count-freeze:{StableHash(raw)}";
    }

    private static string StableHash(string raw)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }
}

public sealed record DispatchWcsTaskCommand(WarehouseTaskId WarehouseTaskId, string AdapterType, string ExternalTaskId, string PayloadJson) : ICommand<WcsTaskId>;

public sealed class DispatchWcsTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<DispatchWcsTaskCommand, WcsTaskId>
{
    public async Task<WcsTaskId> Handle(DispatchWcsTaskCommand request, CancellationToken cancellationToken)
    {
        var warehouseTask = await dbContext.WarehouseTasks.SingleOrDefaultAsync(x => x.Id == request.WarehouseTaskId, cancellationToken)
            ?? throw new KnownException($"Warehouse task was not found: {request.WarehouseTaskId}");
        var adapterType = request.AdapterType.ToLowerInvariant();
        var existing = await dbContext.WcsTasks.SingleOrDefaultAsync(x => x.WarehouseTaskId == request.WarehouseTaskId && x.AdapterType == adapterType, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == WcsTaskStatus.Failed)
            {
                existing.Retry(request.ExternalTaskId, request.PayloadJson);
            }

            return existing.Id;
        }

        var task = WcsTask.Dispatch(warehouseTask.OrganizationId, warehouseTask.EnvironmentId, request.WarehouseTaskId, adapterType, request.ExternalTaskId, request.PayloadJson);
        dbContext.WcsTasks.Add(task);
        return task.Id;
    }
}

public sealed record CompleteWcsTaskCommand(string OrganizationId, string EnvironmentId, string ExternalTaskId, string CompletionPayloadJson) : ICommand;

public sealed class CompleteWcsTaskCommandHandler(
    ApplicationDbContext dbContext,
    ILogger<CompleteWcsTaskCommandHandler>? logger = null)
    : ICommandHandler<CompleteWcsTaskCommand>
{
    public async Task Handle(CompleteWcsTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.WcsTasks.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.ExternalTaskId == request.ExternalTaskId,
                cancellationToken)
            ?? throw new KnownException($"WCS task was not found: {request.ExternalTaskId}");
        if (task.Status == WcsTaskStatus.Completed)
        {
            return;
        }

        var executedQuantity = ExtractExecutedQuantity(request.CompletionPayloadJson, out var diagnosticMessage);
        task.Complete(request.CompletionPayloadJson);
        var warehouseTask = await dbContext.WarehouseTasks.SingleOrDefaultAsync(x => x.Id == task.WarehouseTaskId, cancellationToken)
            ?? throw new KnownException($"Warehouse task was not found: {task.WarehouseTaskId}");
        if (executedQuantity is null)
        {
            logger?.LogWarning(
                "WCS completion callback for external task {ExternalTaskId} did not update warehouse task progress: {DiagnosticMessage}",
                request.ExternalTaskId,
                diagnosticMessage);
            return;
        }

        if (warehouseTask.Status == WarehouseTaskStatus.Completed)
        {
            return;
        }

        warehouseTask.RecordProgress(executedQuantity.Value);
    }

    private static decimal? ExtractExecutedQuantity(string completionPayloadJson, out string diagnosticMessage)
    {
        diagnosticMessage = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(completionPayloadJson);
            var root = document.RootElement;
            foreach (var propertyName in new[] { "actualQuantity", "executedQuantity" })
            {
                if (root.TryGetProperty(propertyName, out var property) && property.TryGetDecimal(out var quantity))
                {
                    return quantity;
                }
            }
        }
        catch (JsonException)
        {
            diagnosticMessage = "Payload is not valid JSON.";
            return null;
        }

        diagnosticMessage = "Payload does not include an explicit executed quantity field.";
        return null;
    }
}

public sealed record FailWcsTaskCommand(string OrganizationId, string EnvironmentId, string ExternalTaskId, string FailureCode, string FailureMessage) : ICommand;

public sealed class FailWcsTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<FailWcsTaskCommand>
{
    public async Task Handle(FailWcsTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.WcsTasks.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.ExternalTaskId == request.ExternalTaskId,
                cancellationToken)
            ?? throw new KnownException($"WCS task was not found: {request.ExternalTaskId}");
        task.Fail(request.FailureCode, request.FailureMessage);
    }
}
