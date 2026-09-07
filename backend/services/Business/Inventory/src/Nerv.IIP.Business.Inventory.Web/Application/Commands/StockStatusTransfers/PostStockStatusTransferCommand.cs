using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Contracts.Inventory;

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
        // 有效上界不是列宽 128，而是「列宽 − handler 追加的最长腿后缀」（#3176）。
        RuleFor(x => x.IdempotencyKey).RequiredInventoryCode(PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength);
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
    /// <summary>状态调拨出库腿后缀。</summary>
    public const string OutboundLegSuffix = ":out";

    /// <summary>状态调拨入库腿后缀。</summary>
    public const string InboundLegSuffix = ":in";

    /// <summary>
    /// 基础幂等键上界 = 幂等键列宽 − 两腿中最长的后缀。校验器直接用它，
    /// 不再用列宽本身——否则 125–128 字符的合法键会通过校验、落库时炸 22001（#3176）。
    /// </summary>
    public static readonly int BaseIdempotencyKeyMaxLength =
        InventoryIdempotencyKeyPolicy.BaseMaxLengthFor(OutboundLegSuffix, InboundLegSuffix);

    public async Task<PostStockStatusTransferResult> Handle(PostStockStatusTransferCommand request, CancellationToken cancellationToken)
    {
        // 状态取值来自 HTTP 写面与集成事件消费者，是外部输入：非法取值走 KnownException（400），
        // 不走 Normalize 的 ArgumentOutOfRangeException（500）（#3186）。
        if (!StockQualityStatus.TryNormalize(request.SourceQualityStatus, out var sourceStatus))
        {
            throw new KnownException(StockQualityStatus.UnsupportedMessage(request.SourceQualityStatus));
        }

        if (!StockQualityStatus.TryNormalize(request.TargetQualityStatus, out var targetStatus))
        {
            throw new KnownException(StockQualityStatus.UnsupportedMessage(request.TargetQualityStatus));
        }

        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        if (sourceStatus == targetStatus)
        {
            throw new KnownException("Source and target stock status must be different.");
        }

        var outboundKey = InventoryIdempotencyKeyPolicy.Compose(request.IdempotencyKey, OutboundLegSuffix);
        var inboundKey = InventoryIdempotencyKeyPolicy.Compose(request.IdempotencyKey, InboundLegSuffix);
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
            InventoryMovementTypes.StatusTransferOut,
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
                expiryDate,
                source.ShelfLifeDays,
                source.ExpiryDateSource);
            dbContext.StockLedgers.Add(target);
        }
        else
        {
            target.MergeExpiryProvenance(source.ShelfLifeDays, source.ExpiryDateSource);
        }

        var inbound = StockMovement.Post(
            request.OrganizationId,
            request.EnvironmentId,
            InventoryMovementTypes.StatusTransferIn,
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
