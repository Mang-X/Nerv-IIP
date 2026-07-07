using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;

public sealed record PostStockStatusTransferCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceQualityStatus,
    string TargetQualityStatus,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null) : ICommand<PostStockStatusTransferResult>;

public sealed record PostStockStatusTransferResult(
    StockMovementId OutboundMovementId,
    StockMovementId InboundMovementId,
    decimal SourceOnHandQuantity,
    decimal TargetOnHandQuantity);

public sealed class PostStockStatusTransferCommandValidator : AbstractValidator<PostStockStatusTransferCommand>
{
    public PostStockStatusTransferCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SourceQualityStatus).RequiredInventoryCode(50);
        RuleFor(x => x.TargetQualityStatus).RequiredInventoryCode(50);
        RuleFor(x => x.SourceService).RequiredInventoryCode(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SourceDocumentLineId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).RequiredInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
        RuleFor(x => x.SkuCode).RequiredInventoryCode(100);
        RuleFor(x => x.UomCode).RequiredInventoryCode(50);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.LocationCode).RequiredInventoryCode(100);
        RuleFor(x => x.LotNo).OptionalInventoryCode(100);
        RuleFor(x => x.SerialNo).OptionalInventoryCode(100);
        RuleFor(x => x.OwnerType).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerId).OptionalInventoryCode(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.ProductionDate!.Value).When(x => x.ProductionDate is not null && x.ExpiryDate is not null);
    }
}

public sealed class PostStockStatusTransferCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<PostStockStatusTransferCommand, PostStockStatusTransferResult>
{
    public async Task<PostStockStatusTransferResult> Handle(PostStockStatusTransferCommand request, CancellationToken cancellationToken)
    {
        var sourceStatus = StockQualityStatus.Normalize(request.SourceQualityStatus);
        var targetStatus = StockQualityStatus.Normalize(request.TargetQualityStatus);
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        if (sourceStatus == targetStatus)
        {
            throw new KnownException("Source and target stock status must be different.");
        }

        var outboundKey = $"{request.IdempotencyKey}:out";
        var inboundKey = $"{request.IdempotencyKey}:in";
        var existingOutbound = await FindMovementAsync(request, outboundKey, cancellationToken);
        var existingInbound = await FindMovementAsync(request, inboundKey, cancellationToken);
        if (existingOutbound is not null && existingInbound is not null)
        {
            var sourceLedger = await FindLedgerAsync(request, sourceStatus, cancellationToken);
            var targetLedger = await FindLedgerAsync(request, targetStatus, cancellationToken);
            return new PostStockStatusTransferResult(
                existingOutbound.Id,
                existingInbound.Id,
                sourceLedger?.OnHandQuantity ?? 0m,
                targetLedger?.OnHandQuantity ?? 0m);
        }

        var source = await FindLedgerAsync(request, sourceStatus, cancellationToken)
            ?? throw new KnownException("Source stock ledger does not exist for the requested status transfer.");
        if (request.Quantity > source.AvailableQuantity)
        {
            throw new KnownException("Status transfer quantity exceeds available stock on the source ledger.");
        }

        var transferUnitCost = source.MovingAverageUnitCost;
        var productionDate = request.ProductionDate ?? source.ProductionDate;
        var expiryDate = request.ExpiryDate ?? source.ExpiryDate;
        var outbound = StockMovement.Post(
            request.OrganizationId,
            request.EnvironmentId,
            "status-transfer-out",
            request.SourceService,
            request.SourceDocumentId,
            request.SourceDocumentLineId,
            outboundKey,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            sourceStatus,
            ownerType,
            request.OwnerId,
            -request.Quantity,
            ProductionDate: productionDate,
            ExpiryDate: expiryDate);
        try
        {
            source.ApplyMovement(outbound);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message);
        }

        dbContext.StockMovements.Add(outbound);

        var target = await FindLedgerAsync(request, targetStatus, productionDate, expiryDate, cancellationToken);
        if (target is null)
        {
            target = StockLedger.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.SkuCode,
                request.UomCode,
                request.SiteCode,
                request.LocationCode,
                request.LotNo,
                request.SerialNo,
                targetStatus,
                ownerType,
                request.OwnerId,
                productionDate,
                expiryDate);
            dbContext.StockLedgers.Add(target);
        }

        var inbound = StockMovement.Post(
            request.OrganizationId,
            request.EnvironmentId,
            "status-transfer-in",
            request.SourceService,
            request.SourceDocumentId,
            request.SourceDocumentLineId,
            inboundKey,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            targetStatus,
            ownerType,
            request.OwnerId,
            request.Quantity,
            transferUnitCost,
            productionDate,
            expiryDate);
        try
        {
            target.ApplyMovement(inbound);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message);
        }

        dbContext.StockMovements.Add(inbound);

        return new PostStockStatusTransferResult(outbound.Id, inbound.Id, source.OnHandQuantity, target.OnHandQuantity);
    }

    private Task<StockMovement?> FindMovementAsync(PostStockStatusTransferCommand request, string idempotencyKey, CancellationToken cancellationToken)
    {
        return dbContext.StockMovements.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceService == request.SourceService
                && x.SourceDocumentId == request.SourceDocumentId
                && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    private Task<StockLedger?> FindLedgerAsync(PostStockStatusTransferCommand request, string qualityStatus, CancellationToken cancellationToken)
    {
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        return FindLedgerAsync(request, qualityStatus, request.ProductionDate, request.ExpiryDate, cancellationToken);
    }

    private Task<StockLedger?> FindLedgerAsync(
        PostStockStatusTransferCommand request,
        string qualityStatus,
        DateOnly? productionDate,
        DateOnly? expiryDate,
        CancellationToken cancellationToken)
    {
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        var query = dbContext.StockLedgers.Where(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SkuCode == request.SkuCode
                && x.UomCode == request.UomCode
                && x.SiteCode == request.SiteCode
                && x.LocationCode == request.LocationCode
                && x.LotNo == request.LotNo
                && x.SerialNo == request.SerialNo
                && x.QualityStatus == qualityStatus
                && x.OwnerType == ownerType
                && x.OwnerId == request.OwnerId);
        if (productionDate is not null)
        {
            query = query.Where(x => x.ProductionDate == productionDate);
        }

        if (expiryDate is not null)
        {
            query = query.Where(x => x.ExpiryDate == expiryDate);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }
}
