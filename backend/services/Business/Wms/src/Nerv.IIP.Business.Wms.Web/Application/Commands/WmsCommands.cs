using Microsoft.EntityFrameworkCore;
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
    string? OwnerId);

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
            request.Lines.Select(x => new InboundOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.ReceivedQuantity, x.StagingLocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId)));
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

public sealed record CompleteWmsMovementResult(InventoryMovementRequestId RequestId, string? InventoryMovementId);

public sealed class CompleteInboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteInboundOrderCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteInboundOrderCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundOrderId, cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        var movementRequest = inbound.Complete(request.IdempotencyKey);
        dbContext.InventoryMovementRequests.Add(movementRequest);
        return new CompleteWmsMovementResult(movementRequest.Id, null);
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
        var inventoryReservationId = line.InventoryReservationId ?? (inventoryReservationClient is null
            ? null
            : (await inventoryReservationClient.ReserveAsync(
                new WmsInventoryReservationRequest(
                    outbound.OrganizationId,
                    outbound.EnvironmentId,
                    "wms",
                    outbound.OutboundOrderNo,
                    line.LineNo,
                    BuildPickingReservationIdempotencyKey(outbound, line.LineNo),
                    line.SkuCode,
                    line.UomCode,
                    outbound.SiteCode,
                    request.FromLocationCode,
                    line.LotNo,
                    line.SerialNo,
                    line.QualityStatus,
                    line.OwnerType,
                    line.OwnerId,
                    request.Quantity),
                cancellationToken)).ReservationId);
        var task = outbound.CreatePickingTask(
            request.TaskNo,
            request.LineNo,
            request.FromLocationCode,
            request.ToLocationCode,
            request.Quantity,
            inventoryReservationId);
        dbContext.WarehouseTasks.Add(task);
        return task.Id;
    }

    private static string BuildPickingReservationIdempotencyKey(OutboundOrder outbound, string lineNo)
    {
        var raw = $"{outbound.OrganizationId}:{outbound.EnvironmentId}:{outbound.OutboundOrderNo}:{lineNo}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
        return $"wms-pick-res:{hash}";
    }
}

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

public sealed class CompleteOutboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteOutboundOrderCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.OutboundOrderId, cancellationToken)
            ?? throw new KnownException($"Outbound order was not found: {request.OutboundOrderId}");
        var movementRequest = outbound.CompletePackReview(request.PackReviewNo, request.Passed, request.IdempotencyKey);
        dbContext.InventoryMovementRequests.Add(movementRequest);
        return new CompleteWmsMovementResult(movementRequest.Id, null);
    }
}

public sealed record CreateCountExecutionCommand(string OrganizationId, string EnvironmentId, string CountNo, string SkuCode, string UomCode, string SiteCode, string LocationCode, decimal ExpectedQuantity) : ICommand<CountExecutionId>;

public sealed class CreateCountExecutionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateCountExecutionCommand, CountExecutionId>
{
    public async Task<CountExecutionId> Handle(CreateCountExecutionCommand request, CancellationToken cancellationToken)
    {
        var count = CountExecution.Create(request.OrganizationId, request.EnvironmentId, request.CountNo, request.SkuCode, request.UomCode, request.SiteCode, request.LocationCode, request.ExpectedQuantity);
        dbContext.CountExecutions.Add(count);
        await Task.CompletedTask;
        return count.Id;
    }
}

public sealed record CompleteCountExecutionCommand(CountExecutionId CountExecutionId, decimal CountedQuantity, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class CompleteCountExecutionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteCountExecutionCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteCountExecutionCommand request, CancellationToken cancellationToken)
    {
        var count = await dbContext.CountExecutions.SingleOrDefaultAsync(x => x.Id == request.CountExecutionId, cancellationToken)
            ?? throw new KnownException($"Count execution was not found: {request.CountExecutionId}");
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

public sealed class MarkInventoryMovementRequestFailedCommandHandler(ApplicationDbContext dbContext)
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
            var outbound = await dbContext.OutboundOrders.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.OutboundOrderNo == request.SourceDocumentId,
                cancellationToken);
            outbound?.MarkInventoryPostingFailed();
        }
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

public sealed class CompleteWcsTaskCommandHandler(ApplicationDbContext dbContext)
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
        task.Complete(request.CompletionPayloadJson);
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
