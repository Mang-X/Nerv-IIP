using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

public sealed class WcsRetryOptions
{
    public int MaxRetryAttempts { get; init; } = WcsTask.MaxRetryAttempts;
    public TimeSpan InitialRetryBackoff { get; init; } = TimeSpan.FromMinutes(1);
    public int CircuitFailureThreshold { get; init; } = WcsTask.MaxRetryAttempts;
}

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
    IReadOnlyCollection<WmsInboundLineInput> Lines,
    string? AssignedOperatorUserId = null,
    string? AssignedPoolCode = null) : ICommand<InboundOrderId>;

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
        RuleFor(x => x.AssignedOperatorUserId).MaximumLength(150);
        RuleFor(x => x.AssignedPoolCode).MaximumLength(100);
    }
}

public sealed class CreateInboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateInboundOrderCommand, InboundOrderId>
{
    public async Task<InboundOrderId> Handle(CreateInboundOrderCommand request, CancellationToken cancellationToken)
    {
        var proposedOrder = InboundOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.InboundOrderNo,
            request.SourceDocumentType,
            request.SourceDocumentId,
            request.SiteCode,
            request.Lines.Select(x => new InboundOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.ReceivedQuantity, x.StagingLocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId, x.ProductionDate, x.ExpiryDate)),
            request.AssignedOperatorUserId,
            request.AssignedPoolCode);
        var existingOrder = await dbContext.InboundOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InboundOrderNo == request.InboundOrderNo,
            cancellationToken);
        if (existingOrder is not null)
        {
            if (!HasSameInboundFacts(existingOrder, proposedOrder))
            {
                throw new KnownException($"Inbound order '{request.InboundOrderNo}' already exists with different inbound facts.");
            }

            return existingOrder.Id;
        }

        dbContext.InboundOrders.Add(proposedOrder);
        return proposedOrder.Id;
    }

    private static bool HasSameInboundFacts(InboundOrder existing, InboundOrder proposed)
    {
        if (existing.SourceDocumentType != proposed.SourceDocumentType
            || existing.SourceDocumentId != proposed.SourceDocumentId
            || existing.SiteCode != proposed.SiteCode
            || existing.AssignedOperatorUserId != proposed.AssignedOperatorUserId
            || existing.AssignedPoolCode != proposed.AssignedPoolCode
            || existing.Lines.Count != proposed.Lines.Count)
        {
            return false;
        }

        var proposedLines = proposed.Lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        return existing.Lines.All(existingLine =>
            proposedLines.TryGetValue(existingLine.LineNo, out var proposedLine)
            && existingLine.SkuCode == proposedLine.SkuCode
            && existingLine.UomCode == proposedLine.UomCode
            && existingLine.ReceivedQuantity == proposedLine.ReceivedQuantity
            && existingLine.StagingLocationCode == proposedLine.StagingLocationCode
            && existingLine.LotNo == proposedLine.LotNo
            && existingLine.SerialNo == proposedLine.SerialNo
            && existingLine.QualityStatus == proposedLine.QualityStatus
            && existingLine.OwnerType == proposedLine.OwnerType
            && existingLine.OwnerId == proposedLine.OwnerId
            && existingLine.ProductionDate == proposedLine.ProductionDate
            && existingLine.ExpiryDate == proposedLine.ExpiryDate);
    }
}

public sealed record CreatePutawayTaskCommand(
    InboundOrderId InboundOrderId,
    string TaskNo,
    string LineNo,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity,
    string? AssignedOperatorUserId = null,
    string? AssignedPoolCode = null) : ICommand<WarehouseTaskId>;

public sealed class CreatePutawayTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreatePutawayTaskCommand, WarehouseTaskId>
{
    public async Task<WarehouseTaskId> Handle(CreatePutawayTaskCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundOrderId, cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        var existingTask = await dbContext.WarehouseTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == inbound.OrganizationId
                && x.EnvironmentId == inbound.EnvironmentId
                && x.TaskNo == request.TaskNo,
            cancellationToken);
        if (existingTask is not null)
        {
            if (existingTask.TaskType != WarehouseTaskType.Putaway
                || existingTask.SourceOrderNo != inbound.InboundOrderNo
                || existingTask.SourceOrderLineNo != request.LineNo
                || existingTask.FromLocationCode != request.FromLocationCode
                || existingTask.ToLocationCode != request.ToLocationCode
                || existingTask.PlannedQuantity != request.Quantity
                || existingTask.AssignedOperatorUserId != request.AssignedOperatorUserId
                || existingTask.AssignedPoolCode != request.AssignedPoolCode)
            {
                throw new KnownException($"Warehouse task '{request.TaskNo}' already exists with different putaway facts.");
            }

            return existingTask.Id;
        }

        var task = inbound.CreatePutawayTask(
            request.TaskNo,
            request.LineNo,
            request.FromLocationCode,
            request.ToLocationCode,
            request.Quantity,
            request.AssignedOperatorUserId,
            request.AssignedPoolCode);
        dbContext.WarehouseTasks.Add(task);
        return task.Id;
    }
}

public sealed record CompleteInboundOrderCommand(
    InboundOrderId InboundOrderId,
    string IdempotencyKey,
    IReadOnlyCollection<InboundOrderLineCapture>? Lines = null,
    string? OrganizationId = null,
    string? EnvironmentId = null) : ICommand<CompleteWmsMovementResult>;

public sealed class CompleteInboundOrderCommandValidator : AbstractValidator<CompleteInboundOrderCommand>
{
    public CompleteInboundOrderCommandValidator() =>
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
}

public sealed class CompleteInboundOrderCommandLock : ICommandLock<CompleteInboundOrderCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        CompleteInboundOrderCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-wms:inbound-order-complete:{command.InboundOrderId}",
            30));
    }
}

public sealed record CompleteWmsMovementResult(InventoryMovementRequestId? RequestId, string? InventoryMovementId);

public sealed class CompleteInboundOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteInboundOrderCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteInboundOrderCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.InboundOrderId
                && (request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
                && (request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId),
            cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        if (inbound.Status == InboundOrderStatus.Cancelled)
        {
            throw new WmsLifecycleConflictException("complete-inbound", inbound.Status.ToString());
        }

        var baseIdempotencyKey = WmsText.IdempotencyKey(request.IdempotencyKey);
        var singleLine = inbound.Lines.Count == 1;
        var replayKeysByLine = inbound.Lines.ToDictionary(
            line => line.LineNo,
            line => singleLine
                ? WmsText.ReplayIdempotencyKeys(request.IdempotencyKey)
                : WmsText.ReplayLineIdempotencyKeys(request.IdempotencyKey, line.LineNo),
            StringComparer.Ordinal);
        var replayKeys = replayKeysByLine.Values.SelectMany(keys => keys).Distinct(StringComparer.Ordinal).ToArray();
        var replayRequests = await dbContext.InventoryMovementRequests
            .Where(x => x.OrganizationId == inbound.OrganizationId
                && x.EnvironmentId == inbound.EnvironmentId
                && x.SourceDocumentId == inbound.InboundOrderNo
                && replayKeys.Contains(x.IdempotencyKey))
            .ToArrayAsync(cancellationToken);
        if (replayRequests.Length > 0)
        {
            if (!HasSameCompletionFacts(inbound, request.Lines, replayKeysByLine, replayRequests))
            {
                throw new WmsIdempotencyConflictException();
            }

            var replayRequest = CanonicalRequest(replayRequests);
            return new CompleteWmsMovementResult(replayRequest.Id, replayRequest.InventoryMovementId);
        }

        if (inbound.Status != InboundOrderStatus.Open)
        {
            throw new WmsLifecycleConflictException("complete-inbound", inbound.Status.ToString());
        }

        var movementRequests = inbound.Complete(baseIdempotencyKey, request.Lines);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        return new CompleteWmsMovementResult(CanonicalRequest(movementRequests).Id, null);
    }

    private static bool HasSameCompletionFacts(
        InboundOrder inbound,
        IReadOnlyCollection<InboundOrderLineCapture>? captures,
        IReadOnlyDictionary<string, IReadOnlyList<string>> replayKeysByLine,
        IReadOnlyCollection<InventoryMovementRequest> replayRequests)
    {
        if (replayRequests.Count != inbound.Lines.Count)
        {
            return false;
        }

        var requestsByLine = new Dictionary<string, InventoryMovementRequest>(StringComparer.Ordinal);
        foreach (var replayRequest in replayRequests)
        {
            if (replayRequest.SourceDocumentLineId is null
                || !requestsByLine.TryAdd(replayRequest.SourceDocumentLineId, replayRequest))
            {
                return false;
            }
        }

        foreach (var line in inbound.Lines)
        {
            if (!requestsByLine.TryGetValue(line.LineNo, out var replayRequest)
                || replayRequest.MovementType != "inbound"
                || !replayKeysByLine[line.LineNo].Contains(replayRequest.IdempotencyKey, StringComparer.Ordinal)
                || replayRequest.SkuCode != line.SkuCode
                || replayRequest.UomCode != line.UomCode
                || replayRequest.SiteCode != inbound.SiteCode
                || replayRequest.LocationCode != line.StagingLocationCode
                || replayRequest.LotNo != line.LotNo
                || replayRequest.SerialNo != line.SerialNo
                || replayRequest.QualityStatus != line.ReceiptQualityStatus
                || replayRequest.OwnerType != line.OwnerType
                || replayRequest.OwnerId != line.OwnerId
                || replayRequest.Quantity != line.ReceivedQuantity
                || replayRequest.ProductionDate != line.ProductionDate
                || replayRequest.ExpiryDate != line.ExpiryDate)
            {
                return false;
            }
        }

        if (captures is null || captures.Count == 0)
        {
            return true;
        }

        var linesByNumber = inbound.Lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        var capturedLineNumbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capture in captures)
        {
            if (string.IsNullOrWhiteSpace(capture.LineNo))
            {
                return false;
            }

            var lineNo = capture.LineNo.Trim();
            if (!capturedLineNumbers.Add(lineNo)
                || !linesByNumber.TryGetValue(lineNo, out var line)
                || WmsText.Optional(capture.LotNo) != line.LotNo
                || capture.ProductionDate != line.ProductionDate
                || capture.ExpiryDate != line.ExpiryDate)
            {
                return false;
            }
        }

        return true;
    }

    private static InventoryMovementRequest CanonicalRequest(IEnumerable<InventoryMovementRequest> requests)
    {
        return requests.OrderBy(x => x.SourceDocumentLineId, StringComparer.Ordinal).First();
    }
}

public sealed record RetryInboundInventoryPostingCommand(InboundOrderId InboundOrderId, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class RetryInboundInventoryPostingCommandValidator : AbstractValidator<RetryInboundInventoryPostingCommand>
{
    public RetryInboundInventoryPostingCommandValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class RetryInboundInventoryPostingCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RetryInboundInventoryPostingCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(RetryInboundInventoryPostingCommand request, CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundOrderId, cancellationToken)
            ?? throw new KnownException($"Inbound order was not found: {request.InboundOrderId}");
        var movementRequests = inbound.RetryInventoryPosting(WmsText.IdempotencyKey(request.IdempotencyKey));
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
    IReadOnlyCollection<WmsOutboundLineInput> Lines,
    string? AssignedOperatorUserId = null,
    string? AssignedPoolCode = null) : ICommand<OutboundOrderId>;

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
            request.Lines.Select(x => new OutboundOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.RequestedQuantity, x.PickLocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId)),
            request.AssignedOperatorUserId,
            request.AssignedPoolCode);
        dbContext.OutboundOrders.Add(order);
        await Task.CompletedTask;
        return order.Id;
    }
}

public sealed record CreatePickingTaskCommand(
    OutboundOrderId OutboundOrderId,
    string TaskNo,
    string LineNo,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity,
    string? AssignedOperatorUserId = null,
    string? AssignedPoolCode = null) : ICommand<WarehouseTaskId>;

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
            reservation?.SerialNo,
            request.AssignedOperatorUserId,
            request.AssignedPoolCode);
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

public sealed class RecordWarehouseTaskProgressCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null,
    ILogger<RecordWarehouseTaskProgressCommandHandler>? logger = null)
    : ICommandHandler<RecordWarehouseTaskProgressCommand>
{
    public async Task Handle(RecordWarehouseTaskProgressCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.WarehouseTasks.SingleOrDefaultAsync(x => x.Id == request.WarehouseTaskId, cancellationToken)
            ?? throw new KnownException($"Warehouse task was not found: {request.WarehouseTaskId}");
        var previouslyExecutedQuantity = task.ExecutedQuantity;
        task.RecordProgress(request.ExecutedQuantity);
        await WarehouseTaskInventoryReservationRenewal.RenewAfterProgressAsync(
            dbContext,
            inventoryReservationClient,
            task,
            previouslyExecutedQuantity,
            logger,
            cancellationToken);
    }
}

internal static class WarehouseTaskInventoryReservationRenewal
{
    public static async Task RenewAfterProgressAsync(
        ApplicationDbContext dbContext,
        IWmsInventoryReservationClient? inventoryReservationClient,
        WarehouseTask task,
        decimal previouslyExecutedQuantity,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (inventoryReservationClient is null ||
            task.TaskType != WarehouseTaskType.Picking ||
            task.Status is not (WarehouseTaskStatus.Open or WarehouseTaskStatus.InProgress) ||
            task.ExecutedQuantity <= previouslyExecutedQuantity)
        {
            return;
        }

        var reservationId = await dbContext.OutboundOrders
            .Where(x => x.OrganizationId == task.OrganizationId
                && x.EnvironmentId == task.EnvironmentId
                && x.OutboundOrderNo == task.SourceOrderNo)
            .SelectMany(x => x.Lines)
            .Where(x => x.LineNo == task.SourceOrderLineNo)
            .Select(x => x.InventoryReservationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(reservationId))
        {
            return;
        }

        try
        {
            await inventoryReservationClient.RenewAsync(
                new WmsInventoryReservationRenewalRequest(reservationId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(
                "Inventory reservation renewal timed out for active WMS picking task {WarehouseTaskId}; preserving recorded task progress.",
                task.Id);
        }
        catch (Exception exception) when (exception is HttpRequestException or KnownException)
        {
            logger?.LogWarning(
                exception,
                "Inventory reservation renewal failed for active WMS picking task {WarehouseTaskId}; preserving recorded task progress.",
                task.Id);
        }
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

public sealed record WarehouseTaskActionResult(
    WarehouseTaskId WarehouseTaskId,
    string TaskType,
    string Status,
    long Version,
    decimal ExecutedQuantity,
    decimal DifferenceQuantity,
    IReadOnlyCollection<string> AllowedActions,
    IReadOnlyCollection<string> BlockReasons);

public interface IWarehouseTaskActionCommand
{
    WarehouseTaskId WarehouseTaskId { get; }

    string OrganizationId { get; }

    string EnvironmentId { get; }

    string ActorUserId { get; }

    string IdempotencyKey { get; }

    long ExpectedVersion { get; }

    WarehouseTaskType ExpectedTaskType { get; }

    IReadOnlyCollection<string>? AuthorizedPoolCodes { get; }

    IReadOnlyCollection<string>? AuthorizedSiteCodes { get; }

    bool OrganizationWideScope { get; }
}

public sealed record StartWarehouseTaskCommand(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorUserId,
    string IdempotencyKey,
    long ExpectedVersion,
    WarehouseTaskType ExpectedTaskType,
    IReadOnlyCollection<string>? AuthorizedPoolCodes = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    bool OrganizationWideScope = false)
    : ICommand<WarehouseTaskActionResult>, IWarehouseTaskActionCommand;

public sealed record RecordWarehouseTaskProgressActionCommand(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorUserId,
    string IdempotencyKey,
    long ExpectedVersion,
    decimal ExecutedQuantity,
    WarehouseTaskType ExpectedTaskType,
    IReadOnlyCollection<string>? AuthorizedPoolCodes = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    bool OrganizationWideScope = false)
    : ICommand<WarehouseTaskActionResult>, IWarehouseTaskActionCommand;

public sealed record ReportWarehouseTaskExceptionCommand(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorUserId,
    string IdempotencyKey,
    long ExpectedVersion,
    string ExceptionCode,
    string Reason,
    WarehouseTaskType ExpectedTaskType,
    IReadOnlyCollection<string>? AuthorizedPoolCodes = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    bool OrganizationWideScope = false)
    : ICommand<WarehouseTaskActionResult>, IWarehouseTaskActionCommand;

public sealed record CompleteWarehouseTaskActionCommand(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorUserId,
    string IdempotencyKey,
    long ExpectedVersion,
    decimal ExecutedQuantity,
    string? DifferenceReason,
    WarehouseTaskType ExpectedTaskType,
    IReadOnlyCollection<string>? AuthorizedPoolCodes = null,
    IReadOnlyCollection<string>? AuthorizedSiteCodes = null,
    bool OrganizationWideScope = false)
    : ICommand<WarehouseTaskActionResult>, IWarehouseTaskActionCommand;

public sealed class StartWarehouseTaskCommandValidator : AbstractValidator<StartWarehouseTaskCommand>
{
    public StartWarehouseTaskCommandValidator() => WarehouseTaskActionValidation.Configure(this);
}

public sealed class RecordWarehouseTaskProgressActionCommandValidator
    : AbstractValidator<RecordWarehouseTaskProgressActionCommand>
{
    public RecordWarehouseTaskProgressActionCommandValidator()
    {
        WarehouseTaskActionValidation.Configure(this);
        RuleFor(x => x.ExecutedQuantity).GreaterThanOrEqualTo(0);
    }
}

public sealed class ReportWarehouseTaskExceptionCommandValidator
    : AbstractValidator<ReportWarehouseTaskExceptionCommand>
{
    public ReportWarehouseTaskExceptionCommandValidator()
    {
        WarehouseTaskActionValidation.Configure(this);
        RuleFor(x => x.ExceptionCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CompleteWarehouseTaskActionCommandValidator
    : AbstractValidator<CompleteWarehouseTaskActionCommand>
{
    public CompleteWarehouseTaskActionCommandValidator()
    {
        WarehouseTaskActionValidation.Configure(this);
        RuleFor(x => x.ExecutedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DifferenceReason).MaximumLength(1000);
    }
}

internal static class WarehouseTaskActionValidation
{
    public static void Configure<TCommand>(AbstractValidator<TCommand> validator)
        where TCommand : IWarehouseTaskActionCommand
    {
        validator.RuleFor(x => x.WarehouseTaskId).NotEmpty();
        validator.RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.ActorUserId).NotEmpty().MaximumLength(150);
        validator.RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        validator.RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        validator.RuleForEach(x => x.AuthorizedPoolCodes).NotEmpty().MaximumLength(100);
        validator.RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
    }
}

public sealed class StartWarehouseTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<StartWarehouseTaskCommand, WarehouseTaskActionResult>
{
    public Task<WarehouseTaskActionResult> Handle(
        StartWarehouseTaskCommand request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionExecution.ExecuteAsync(
            dbContext,
            request,
            "start",
            new
            {
                request.ActorUserId,
                request.ExpectedVersion,
                request.ExpectedTaskType,
            },
            task => task.Start(
                request.ActorUserId,
                request.ExpectedVersion,
                claimPoolAssignment: task.AssignedOperatorUserId is null),
            cancellationToken);
}

public sealed class RecordWarehouseTaskProgressActionCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null,
    ILogger<RecordWarehouseTaskProgressActionCommandHandler>? logger = null)
    : ICommandHandler<RecordWarehouseTaskProgressActionCommand, WarehouseTaskActionResult>
{
    public async Task<WarehouseTaskActionResult> Handle(
        RecordWarehouseTaskProgressActionCommand request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.WarehouseTasks.SingleOrDefaultAsync(
            x => x.Id == request.WarehouseTaskId
                && x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId,
            cancellationToken);
        var previouslyExecutedQuantity = task?.ExecutedQuantity ?? 0m;
        var result = await WarehouseTaskActionExecution.ExecuteAsync(
            dbContext,
            request,
            "progress",
            new
            {
                request.ActorUserId,
                request.ExpectedVersion,
                request.ExpectedTaskType,
                request.ExecutedQuantity,
            },
            task => task.RecordProgress(
                request.ExecutedQuantity,
                request.ActorUserId,
                request.ExpectedVersion),
            cancellationToken);
        await WarehouseTaskInventoryReservationRenewal.RenewAfterProgressAsync(
            dbContext,
            inventoryReservationClient,
            task!,
            previouslyExecutedQuantity,
            logger,
            cancellationToken);
        return result;
    }
}

public sealed class ReportWarehouseTaskExceptionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReportWarehouseTaskExceptionCommand, WarehouseTaskActionResult>
{
    public Task<WarehouseTaskActionResult> Handle(
        ReportWarehouseTaskExceptionCommand request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionExecution.ExecuteAsync(
            dbContext,
            request,
            "exception",
            new
            {
                request.ActorUserId,
                request.ExpectedVersion,
                request.ExpectedTaskType,
                request.ExceptionCode,
                request.Reason,
            },
            task => task.ReportException(
                request.ExceptionCode,
                request.Reason,
                request.ActorUserId,
                request.ExpectedVersion),
            cancellationToken);
}

public sealed class CompleteWarehouseTaskActionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteWarehouseTaskActionCommand, WarehouseTaskActionResult>
{
    public Task<WarehouseTaskActionResult> Handle(
        CompleteWarehouseTaskActionCommand request,
        CancellationToken cancellationToken) =>
        WarehouseTaskActionExecution.ExecuteAsync(
            dbContext,
            request,
            "complete",
            new
            {
                request.ActorUserId,
                request.ExpectedVersion,
                request.ExpectedTaskType,
                request.ExecutedQuantity,
                request.DifferenceReason,
            },
            task => task.Complete(
                request.ExecutedQuantity,
                request.ActorUserId,
                request.DifferenceReason,
                request.ExpectedVersion),
            cancellationToken);
}

public sealed class WarehouseTaskActionCommandLock<TCommand> : ICommandLock<TCommand>
    where TCommand : IBaseCommand, IWarehouseTaskActionCommand
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        TCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-wms:warehouse-task-action:{command.WarehouseTaskId}",
            30));
    }
}

internal static class WarehouseTaskActionExecution
{
    public static async Task<WarehouseTaskActionResult> ExecuteAsync<TCommand>(
        ApplicationDbContext dbContext,
        TCommand command,
        string action,
        object payload,
        Action<WarehouseTask> mutate,
        CancellationToken cancellationToken)
        where TCommand : IWarehouseTaskActionCommand
    {
        var task = await dbContext.WarehouseTasks.SingleOrDefaultAsync(
            x => x.Id == command.WarehouseTaskId
                && x.OrganizationId == command.OrganizationId
                && x.EnvironmentId == command.EnvironmentId,
            cancellationToken)
            ?? throw new WmsLifecycleConflictException(action, "task-not-found-or-scope-mismatch");
        EnsureTaskType(task, command.ExpectedTaskType, action);
        EnsureActorCanExecute(task, command, action);

        var normalizedIdempotencyKey = WmsText.IdempotencyKey(command.IdempotencyKey);
        var fingerprint = Fingerprint(payload);
        var existingReceipt = await dbContext.WarehouseTaskActionReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == command.OrganizationId
                    && x.EnvironmentId == command.EnvironmentId
                    && x.WarehouseTaskId == command.WarehouseTaskId
                    && x.Action == action
                    && x.IdempotencyKey == normalizedIdempotencyKey,
                cancellationToken);
        if (existingReceipt is not null)
        {
            if (!existingReceipt.MatchesPayload(fingerprint))
            {
                throw new WmsIdempotencyConflictException();
            }

            return FromReceipt(task.TaskType, existingReceipt);
        }

        try
        {
            mutate(task);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            throw new WmsLifecycleConflictException(action, task.Status.ToString());
        }

        var result = FromTask(task);
        dbContext.WarehouseTaskActionReceipts.Add(WarehouseTaskActionReceipt.Create(
            command.OrganizationId,
            command.EnvironmentId,
            command.WarehouseTaskId,
            action,
            normalizedIdempotencyKey,
            fingerprint,
            result.Status,
            result.Version,
            result.ExecutedQuantity,
            result.DifferenceQuantity));
        return result;
    }

    private static void EnsureTaskType(
        WarehouseTask task,
        WarehouseTaskType expectedTaskType,
        string action)
    {
        if (task.TaskType != expectedTaskType)
        {
            throw new WmsLifecycleConflictException(action, "task-type-mismatch");
        }
    }

    private static void EnsureActorCanExecute<TCommand>(
        WarehouseTask task,
        TCommand command,
        string action)
        where TCommand : IWarehouseTaskActionCommand
    {
        var actorUserId = WmsText.Required(command.ActorUserId, nameof(command.ActorUserId));
        if (!string.IsNullOrWhiteSpace(task.AssignedOperatorUserId))
        {
            if (!string.Equals(task.AssignedOperatorUserId, actorUserId, StringComparison.Ordinal))
            {
                throw new WmsLifecycleConflictException(action, "assignment-mismatch");
            }

            return;
        }

        var authorizedPoolCodes = NormalizeScopes(command.AuthorizedPoolCodes);
        if (!string.IsNullOrWhiteSpace(task.AssignedPoolCode))
        {
            if (!authorizedPoolCodes.Contains(task.AssignedPoolCode))
            {
                throw new WmsLifecycleConflictException(action, "pool-scope-mismatch");
            }

            return;
        }

        if (command.OrganizationWideScope)
        {
            return;
        }

        var authorizedSiteCodes = NormalizeScopes(command.AuthorizedSiteCodes);
        if (!authorizedSiteCodes.Contains(task.SiteCode))
        {
            throw new WmsLifecycleConflictException(action, "site-scope-mismatch");
        }
    }

    private static HashSet<string> NormalizeScopes(IEnumerable<string>? values) =>
        values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    private static string Fingerprint(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static WarehouseTaskActionResult FromTask(WarehouseTask task)
    {
        var status = task.Status.ToString();
        var allowedActions = status == WarehouseTaskStatus.Open.ToString()
            ? new[] { "start" }
            : status == WarehouseTaskStatus.InProgress.ToString()
                ? new[] { "progress", "exception", "complete" }
                : [];
        var blockReasons = allowedActions.Length == 0
            ? new[] { "TASK_TERMINAL" }
            : [];
        return new WarehouseTaskActionResult(
            task.Id,
            task.TaskType.ToString(),
            status,
            task.Version,
            task.ExecutedQuantity,
            Math.Max(0, task.PlannedQuantity - task.ExecutedQuantity),
            allowedActions,
            blockReasons);
    }

    private static WarehouseTaskActionResult FromReceipt(
        WarehouseTaskType taskType,
        WarehouseTaskActionReceipt receipt)
    {
        var allowedActions = receipt.ResultStatus == WarehouseTaskStatus.Open.ToString()
            ? new[] { "start" }
            : receipt.ResultStatus == WarehouseTaskStatus.InProgress.ToString()
                ? new[] { "progress", "exception", "complete" }
                : [];
        var blockReasons = allowedActions.Length == 0
            ? new[] { "TASK_TERMINAL" }
            : [];
        return new WarehouseTaskActionResult(
            receipt.WarehouseTaskId,
            taskType.ToString(),
            receipt.ResultStatus,
            receipt.ResultVersion,
            receipt.ResultExecutedQuantity,
            receipt.ResultDifferenceQuantity,
            allowedActions,
            blockReasons);
    }
}

public sealed class WarehouseTaskActionPersistenceConflictMiddleware(
    RequestDelegate next,
    ILogger<WarehouseTaskActionPersistenceConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException exception) when (
            exception.Entries.Any(entry => entry.Entity is WarehouseTask))
        {
            logger.LogInformation(
                "WMS warehouse-task optimistic concurrency conflict. Path={Path}",
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, "warehouse-task-concurrency-conflict"),
                context.RequestAborted);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is WarehouseTaskActionReceipt)
            && HasPostgreSqlUniqueViolation(exception))
        {
            logger.LogInformation(
                "WMS warehouse-task action receipt uniqueness conflict. Path={Path}",
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
    }

    private static bool HasPostgreSqlUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            if (string.Equals(sqlState, "23505", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record CompleteOutboundOrderCommand(
    OutboundOrderId OutboundOrderId,
    string PackReviewNo,
    bool Passed,
    string IdempotencyKey,
    string? OrganizationId = null,
    string? EnvironmentId = null) : ICommand<CompleteWmsMovementResult>;

public sealed class CompleteOutboundOrderCommandValidator : AbstractValidator<CompleteOutboundOrderCommand>
{
    public CompleteOutboundOrderCommandValidator()
    {
        RuleFor(x => x.PackReviewNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class CompleteOutboundOrderCommandLock : ICommandLock<CompleteOutboundOrderCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        CompleteOutboundOrderCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-wms:outbound-order-complete:{command.OutboundOrderId}",
            30));
    }
}

public sealed record CloseBackorderOrderCommand(BackorderOrderId BackorderOrderId, string Reason) : ICommand;

public sealed class CloseBackorderOrderCommandValidator : AbstractValidator<CloseBackorderOrderCommand>
{
    public CloseBackorderOrderCommandValidator()
    {
        RuleFor(x => x.BackorderOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CloseBackorderOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CloseBackorderOrderCommand>
{
    public async Task Handle(CloseBackorderOrderCommand request, CancellationToken cancellationToken)
    {
        var backorder = await dbContext.BackorderOrders.SingleOrDefaultAsync(x => x.Id == request.BackorderOrderId, cancellationToken)
            ?? throw new KnownException($"Backorder order was not found: {request.BackorderOrderId}");
        try
        {
            backorder.Close(request.Reason);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}

public sealed class CompleteOutboundOrderCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CompleteOutboundOrderCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteOutboundOrderCommand request, CancellationToken cancellationToken)
    {
        var baseIdempotencyKey = WmsText.IdempotencyKey(request.IdempotencyKey);
        var outbound = await dbContext.OutboundOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.OutboundOrderId
                && (request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
                && (request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId),
            cancellationToken)
            ?? throw new KnownException($"Outbound order was not found: {request.OutboundOrderId}");
        if (outbound.Status is OutboundOrderStatus.Completed or OutboundOrderStatus.InventoryPostingPending)
        {
            if (!string.Equals(outbound.PackReviewNo, request.PackReviewNo.Trim(), StringComparison.Ordinal) ||
                outbound.PackReviewPassed != request.Passed)
            {
                throw new WmsIdempotencyConflictException();
            }

            var existingRequests = await dbContext.InventoryMovementRequests
                .Where(x => x.OrganizationId == outbound.OrganizationId
                    && x.EnvironmentId == outbound.EnvironmentId
                    && x.SourceDocumentId == outbound.OutboundOrderNo)
                .OrderBy(x => x.SourceDocumentLineId)
                .ToArrayAsync(cancellationToken);
            var singleLine = existingRequests.Length == 1;
            var matchesSuppliedKey = existingRequests.Length > 0
                && existingRequests.All(x =>
                    (singleLine
                        ? WmsText.ReplayIdempotencyKeys(request.IdempotencyKey)
                        : WmsText.ReplayLineIdempotencyKeys(
                            request.IdempotencyKey,
                            x.SourceDocumentLineId
                                ?? throw new KnownException("Completed outbound order contains an invalid movement line.")))
                    .Contains(x.IdempotencyKey, StringComparer.Ordinal));
            if (!matchesSuppliedKey)
            {
                throw new WmsIdempotencyConflictException();
            }

            var existingRequest = existingRequests[0];
            return new CompleteWmsMovementResult(existingRequest.Id, existingRequest.InventoryMovementId);
        }

        if (outbound.Status != OutboundOrderStatus.Open)
        {
            throw new WmsLifecycleConflictException("complete-outbound", outbound.Status.ToString());
        }

        var executedQuantitiesByLine = await GetExecutedPickingQuantitiesAsync(outbound, cancellationToken);
        EnsureInventoryClientAvailableForShortPickRelease(outbound, executedQuantitiesByLine);
        var movementRequests = outbound.CompletePackReview(request.PackReviewNo, request.Passed, baseIdempotencyKey, executedQuantitiesByLine);
        await ReleaseShortPickedReservationBalancesAsync(outbound, cancellationToken);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        foreach (var line in outbound.Lines.Where(x => x.BackorderQuantity > 0))
        {
            var backorderNo = WmsText.StableOperationalCode("BO", outbound.OutboundOrderNo, line.LineNo);
            var backorder = BackorderOrder.Create(
                outbound.OrganizationId,
                outbound.EnvironmentId,
                backorderNo,
                outbound.OutboundOrderNo,
                line.LineNo,
                line.SkuCode,
                line.UomCode,
                outbound.SiteCode,
                line.PickLocationCode,
                line.BackorderQuantity);
            dbContext.BackorderOrders.Add(backorder);
            dbContext.WarehouseTasks.Add(backorder.CreateReplenishmentRecommendation(WmsText.StableOperationalCode("RPL", outbound.OutboundOrderNo, line.LineNo)));
        }

        return new CompleteWmsMovementResult(movementRequests.First().Id, null);
    }

    private async Task<IReadOnlyDictionary<string, decimal>> GetExecutedPickingQuantitiesAsync(
        OutboundOrder outbound,
        CancellationToken cancellationToken)
    {
        var taskExecutions = await dbContext.WarehouseTasks
            .Where(x => x.OrganizationId == outbound.OrganizationId
                && x.EnvironmentId == outbound.EnvironmentId
                && x.TaskType == WarehouseTaskType.Picking
                && x.SourceOrderNo == outbound.OutboundOrderNo)
            .Select(x => new
            {
                LineNo = x.SourceOrderLineNo,
                x.Status,
                x.ExecutedQuantity,
                x.CompletionReason,
            })
            .ToArrayAsync(cancellationToken);

        if (taskExecutions.Length == 0
            || taskExecutions.Any(x =>
                x.Status is not (WarehouseTaskStatus.Completed or WarehouseTaskStatus.CompletedWithDifference))
            || taskExecutions.Any(x =>
                x.Status == WarehouseTaskStatus.CompletedWithDifference
                && string.IsNullOrWhiteSpace(x.CompletionReason)))
        {
            throw new KnownException(
                "Outbound order requires terminal picking task execution facts with a persisted difference reason before pack review.");
        }

        var executedQuantities = taskExecutions
            .GroupBy(x => x.LineNo, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(task => task.ExecutedQuantity),
                StringComparer.Ordinal);
        if (outbound.Lines.Any(line => !executedQuantities.ContainsKey(line.LineNo)))
        {
            throw new KnownException(
                "Every outbound order line requires a terminal picking task execution fact before pack review.");
        }

        return executedQuantities;
    }

    private void EnsureInventoryClientAvailableForShortPickRelease(
        OutboundOrder outbound,
        IReadOnlyDictionary<string, decimal> executedQuantitiesByLine)
    {
        if (inventoryReservationClient is not null)
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
                && (x.Status == WarehouseTaskStatus.Open
                    || x.Status == WarehouseTaskStatus.InProgress))
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

public sealed record CancelInboundOrdersForSourceCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceDocumentType,
    string SourceDocumentId,
    string Reason) : ICommand<int>;

public sealed class CancelInboundOrdersForSourceCommandValidator : AbstractValidator<CancelInboundOrdersForSourceCommand>
{
    public CancelInboundOrdersForSourceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CancelInboundOrdersForSourceCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CancelInboundOrdersForSourceCommand, int>
{
    public async Task<int> Handle(CancelInboundOrdersForSourceCommand request, CancellationToken cancellationToken)
    {
        var sourceDocumentType = WmsText.Required(request.SourceDocumentType, nameof(request.SourceDocumentType));
        var sourceDocumentId = WmsText.Required(request.SourceDocumentId, nameof(request.SourceDocumentId));
        var reason = WmsText.Required(request.Reason, nameof(request.Reason));
        var inboundOrders = await dbContext.InboundOrders
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceDocumentType == sourceDocumentType
                && x.SourceDocumentId == sourceDocumentId
                && x.Status == InboundOrderStatus.Open)
            .ToArrayAsync(cancellationToken);
        var inboundOrderNos = inboundOrders.Select(x => x.InboundOrderNo).ToArray();
        var openPutawayTasks = await dbContext.WarehouseTasks
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.TaskType == WarehouseTaskType.Putaway
                && inboundOrderNos.Contains(x.SourceOrderNo)
                && (x.Status == WarehouseTaskStatus.Open
                    || x.Status == WarehouseTaskStatus.InProgress))
            .ToArrayAsync(cancellationToken);
        var openPutawayTaskIds = openPutawayTasks.Select(x => x.Id).ToArray();
        var cancellableWcsTasks = await dbContext.WcsTasks
            .Where(x => openPutawayTaskIds.Contains(x.WarehouseTaskId) && x.Status != WcsTaskStatus.Completed)
            .ToArrayAsync(cancellationToken);

        foreach (var inboundOrder in inboundOrders)
        {
            inboundOrder.Cancel(reason);
        }

        foreach (var task in openPutawayTasks)
        {
            task.Cancel();
        }

        foreach (var task in cancellableWcsTasks)
        {
            task.Cancel();
        }

        return inboundOrders.Length;
    }
}

public sealed record RetryOutboundInventoryPostingCommand(OutboundOrderId OutboundOrderId, string IdempotencyKey) : ICommand<CompleteWmsMovementResult>;

public sealed class RetryOutboundInventoryPostingCommandValidator : AbstractValidator<RetryOutboundInventoryPostingCommand>
{
    public RetryOutboundInventoryPostingCommandValidator()
    {
        RuleFor(x => x.OutboundOrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
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
                    WmsInventoryReservationIdempotencyKeys.ForOutboundRetry(
                        outbound,
                        line.LineNo,
                        WmsText.IdempotencyKey(request.IdempotencyKey)),
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

        var movementRequests = outbound.RetryInventoryPosting(
            WmsText.IdempotencyKey(request.IdempotencyKey),
            reservationIds);
        dbContext.InventoryMovementRequests.AddRange(movementRequests);
        return new CompleteWmsMovementResult(movementRequests.First().Id, null);
    }
}

public sealed record CreateCountExecutionCommand(
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal ExpectedQuantity,
    string? AssignedOperatorUserId = null,
    string? AssignedPoolCode = null) : ICommand<CountExecutionId>;

public sealed class CreateCountExecutionCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CreateCountExecutionCommand, CountExecutionId>
{
    public async Task<CountExecutionId> Handle(CreateCountExecutionCommand request, CancellationToken cancellationToken)
    {
        var count = CountExecution.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.CountNo,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.ExpectedQuantity,
            request.AssignedOperatorUserId,
            request.AssignedPoolCode);
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

public sealed record CompleteCountExecutionCommand(
    CountExecutionId CountExecutionId,
    decimal CountedQuantity,
    string IdempotencyKey,
    string? OrganizationId = null,
    string? EnvironmentId = null) : ICommand<CompleteWmsMovementResult>;

public sealed class CompleteCountExecutionCommandValidator : AbstractValidator<CompleteCountExecutionCommand>
{
    public CompleteCountExecutionCommandValidator()
    {
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class CompleteCountExecutionCommandLock : ICommandLock<CompleteCountExecutionCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        CompleteCountExecutionCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandLockSettings(
            $"business-wms:count-execution-complete:{command.CountExecutionId}",
            30));
    }
}

public sealed class CompleteCountExecutionCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInventoryReservationClient? inventoryReservationClient = null)
    : ICommandHandler<CompleteCountExecutionCommand, CompleteWmsMovementResult>
{
    public async Task<CompleteWmsMovementResult> Handle(CompleteCountExecutionCommand request, CancellationToken cancellationToken)
    {
        var baseIdempotencyKey = WmsText.IdempotencyKey(request.IdempotencyKey);
        var replayKeys = WmsText.ReplayIdempotencyKeys(request.IdempotencyKey);
        var count = await dbContext.CountExecutions.SingleOrDefaultAsync(
            x => x.Id == request.CountExecutionId
                && (request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
                && (request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId),
            cancellationToken)
            ?? throw new KnownException($"Count execution was not found: {request.CountExecutionId}");
        var priorRequests = await dbContext.InventoryMovementRequests
            .Where(x =>
                x.OrganizationId == count.OrganizationId &&
                x.EnvironmentId == count.EnvironmentId &&
                x.MovementType == "count-adjustment" &&
                x.SourceDocumentId == count.CountNo &&
                x.SourceDocumentLineId == null)
            .ToArrayAsync(cancellationToken);
        var replay = priorRequests
            .Where(x => replayKeys.Contains(x.IdempotencyKey, StringComparer.Ordinal))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
        if (replay is not null)
        {
            EnsureSameCountPayload(count, request, replay);
            return new CompleteWmsMovementResult(replay.Id, replay.InventoryMovementId);
        }

        if (priorRequests.Length > 0)
        {
            throw new WmsIdempotencyConflictException();
        }

        if (count.Status != CountExecutionStatus.Open)
        {
            throw new WmsLifecycleConflictException("complete-count", count.Status.ToString());
        }

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
                new WmsInventoryCountAdjustmentRequest(count.InventoryCountTaskId!, request.CountedQuantity, baseIdempotencyKey),
                cancellationToken);
            count.Complete(request.CountedQuantity);
            var postedReceipt = InventoryMovementRequest.RecordPosted(
                count.OrganizationId,
                count.EnvironmentId,
                "count-adjustment",
                count.CountNo,
                baseIdempotencyKey,
                count.SkuCode,
                count.UomCode,
                count.SiteCode,
                count.LocationCode,
                "qualified",
                "company",
                count.VarianceQuantity
                    ?? throw new KnownException("Count execution variance was not calculated."),
                adjustment.MovementId);
            dbContext.InventoryMovementRequests.Add(postedReceipt);
            return new CompleteWmsMovementResult(postedReceipt.Id, adjustment.MovementId);
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
            baseIdempotencyKey,
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

    private static void EnsureSameCountPayload(
        CountExecution count,
        CompleteCountExecutionCommand request,
        InventoryMovementRequest replay)
    {
        var expectedVariance = request.CountedQuantity - count.ExpectedQuantity;
        if (count.CountedQuantity != request.CountedQuantity ||
            replay.Quantity != expectedVariance ||
            !string.Equals(replay.SkuCode, count.SkuCode, StringComparison.Ordinal) ||
            !string.Equals(replay.UomCode, count.UomCode, StringComparison.Ordinal) ||
            !string.Equals(replay.SiteCode, count.SiteCode, StringComparison.Ordinal) ||
            !string.Equals(replay.LocationCode, count.LocationCode, StringComparison.Ordinal))
        {
            throw new WmsIdempotencyConflictException();
        }
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
        if (!string.Equals(request.MovementType, "outbound", StringComparison.Ordinal)
            || movementRequest.SourceDocumentLineId is null)
        {
            return;
        }

        var outbound = await dbContext.OutboundOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.OutboundOrderNo == request.SourceDocumentId,
                cancellationToken);
        if (outbound is null || outbound.Status != OutboundOrderStatus.InventoryPostingPending)
        {
            return;
        }

        outbound.RecordInventoryPostingProgress();
        var postingRequests = await dbContext.InventoryMovementRequests
            .Where(x => x.OrganizationId == outbound.OrganizationId
                && x.EnvironmentId == outbound.EnvironmentId
                && x.MovementType == "outbound"
                && x.SourceDocumentId == outbound.OutboundOrderNo)
            .ToArrayAsync(cancellationToken);
        var latestRequestsByLine = InventoryMovementRequestAttempts.LatestByLine(postingRequests);
        var postedLines = outbound.Lines.Where(x => x.IssuedQuantity > 0).ToArray();
        if (postedLines.Length > 0
            && postedLines.All(line =>
                latestRequestsByLine.TryGetValue(line.LineNo, out var latestRequest)
                && latestRequest.Status == InventoryMovementRequestStatus.Posted))
        {
            outbound.MarkInventoryPostingCompleted();
        }
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

public sealed record DispatchWcsTaskCommand(WarehouseTaskId WarehouseTaskId, string AdapterType, string ExternalTaskId, string PayloadJson, string? DeviceId = null) : ICommand<WcsTaskId>;

public sealed class DispatchWcsTaskCommandHandler(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    : ICommandHandler<DispatchWcsTaskCommand, WcsTaskId>
{
    public async Task<WcsTaskId> Handle(DispatchWcsTaskCommand request, CancellationToken cancellationToken)
    {
        var warehouseTask = await dbContext.WarehouseTasks.SingleOrDefaultAsync(x => x.Id == request.WarehouseTaskId, cancellationToken)
            ?? throw new KnownException($"Warehouse task was not found: {request.WarehouseTaskId}");
        var adapterType = request.AdapterType.ToLowerInvariant();
        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? adapterType : request.DeviceId.Trim();
        var circuit = await dbContext.WcsDispatchCircuits.SingleOrDefaultAsync(
            x => x.OrganizationId == warehouseTask.OrganizationId
                && x.EnvironmentId == warehouseTask.EnvironmentId
                && x.AdapterType == adapterType
                && x.DeviceId == deviceId,
            cancellationToken);
        if (circuit?.IsOpen is true)
        {
            throw new KnownException(circuit.RejectionReason!);
        }

        var existing = await dbContext.WcsTasks.SingleOrDefaultAsync(x => x.WarehouseTaskId == request.WarehouseTaskId && x.AdapterType == adapterType, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == WcsTaskStatus.Failed)
            {
                try
                {
                    existing.Retry(request.ExternalTaskId, request.PayloadJson, (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
                }
                catch (InvalidOperationException exception)
                {
                    throw new KnownException(exception.Message);
                }
            }

            return existing.Id;
        }

        var task = WcsTask.Dispatch(warehouseTask.OrganizationId, warehouseTask.EnvironmentId, request.WarehouseTaskId, adapterType, request.ExternalTaskId, request.PayloadJson, deviceId);
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
        var circuit = await dbContext.WcsDispatchCircuits.SingleOrDefaultAsync(
            x => x.OrganizationId == task.OrganizationId
                && x.EnvironmentId == task.EnvironmentId
                && x.AdapterType == task.AdapterType
                && x.DeviceId == task.DeviceId,
            cancellationToken);
        circuit?.RecordSuccess();
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

public sealed class FailWcsTaskCommandHandler(
    ApplicationDbContext dbContext,
    TimeProvider? timeProvider = null,
    IOptions<WcsRetryOptions>? retryOptions = null)
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
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var options = retryOptions?.Value ?? new WcsRetryOptions();
        if (!task.Fail(request.FailureCode, request.FailureMessage, now, options.MaxRetryAttempts, options.InitialRetryBackoff))
        {
            return;
        }
        var circuit = await dbContext.WcsDispatchCircuits.SingleOrDefaultAsync(
            x => x.OrganizationId == task.OrganizationId
                && x.EnvironmentId == task.EnvironmentId
                && x.AdapterType == task.AdapterType
                && x.DeviceId == task.DeviceId,
            cancellationToken);
        if (circuit is null)
        {
            circuit = WcsDispatchCircuit.Create(task.OrganizationId, task.EnvironmentId, task.AdapterType, task.DeviceId);
            dbContext.WcsDispatchCircuits.Add(circuit);
        }

        circuit.RecordFailure(now, options.CircuitFailureThreshold);
    }
}

public sealed record ResetWcsDispatchCircuitCommand(string OrganizationId, string EnvironmentId, string AdapterType, string DeviceId) : ICommand;

public sealed class ResetWcsDispatchCircuitCommandHandler(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    : ICommandHandler<ResetWcsDispatchCircuitCommand>
{
    public async Task Handle(ResetWcsDispatchCircuitCommand request, CancellationToken cancellationToken)
    {
        var circuit = await dbContext.WcsDispatchCircuits.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.AdapterType == request.AdapterType.ToLowerInvariant() && x.DeviceId == request.DeviceId, cancellationToken)
            ?? throw new KnownException($"WCS dispatch circuit was not found for adapter '{request.AdapterType}' and device '{request.DeviceId}'.");
        circuit.Reset((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
    }
}
