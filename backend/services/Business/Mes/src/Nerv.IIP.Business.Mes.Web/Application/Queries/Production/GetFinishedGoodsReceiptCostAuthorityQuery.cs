using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

public sealed record GetFinishedGoodsReceiptCostAuthorityQuery(
    MesFinishedGoodsReceiptCostAuthorityRequest Request)
    : IQuery<MesFinishedGoodsReceiptCostAuthorityResponse>;

public sealed class GetFinishedGoodsReceiptCostAuthorityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetFinishedGoodsReceiptCostAuthorityQuery, MesFinishedGoodsReceiptCostAuthorityResponse>
{
    public async Task<MesFinishedGoodsReceiptCostAuthorityResponse> Handle(
        GetFinishedGoodsReceiptCostAuthorityQuery query,
        CancellationToken cancellationToken)
    {
        var request = query.Request;
        if (string.IsNullOrWhiteSpace(request.OrganizationId) ||
            string.IsNullOrWhiteSpace(request.EnvironmentId) ||
            string.IsNullOrWhiteSpace(request.ReceiptRequestNo) ||
            string.IsNullOrWhiteSpace(request.WorkOrderId) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Rejected("invalid-authority-scope");
        }

        var receipt = await dbContext.FinishedGoodsReceiptRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.RequestNo == request.ReceiptRequestNo &&
                    x.WorkOrderId == request.WorkOrderId,
                cancellationToken);
        if (receipt is null)
        {
            return Rejected("receipt-not-found");
        }

        if (!FinishedGoodsReceiptRequest.IsInventoryPostingIdempotencyKey(
                request.OrganizationId,
                request.EnvironmentId,
                receipt.RequestNo,
                request.IdempotencyKey))
        {
            return Rejected("idempotency-scope-mismatch");
        }

        var workOrder = await dbContext.WorkOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderIdValue == request.WorkOrderId,
                cancellationToken);
        if (workOrder is null)
        {
            return Rejected("work-order-not-found");
        }

        if (!string.Equals(receipt.SkuId, workOrder.SkuId, StringComparison.Ordinal) ||
            (workOrder.UomCode is not null && !string.Equals(receipt.UomCode, workOrder.UomCode, StringComparison.Ordinal)))
        {
            return Rejected("receipt-work-order-mismatch");
        }

        var capitalizationIdempotencyKey =
            $"work-order-cost-capitalized:{request.OrganizationId}:{request.EnvironmentId}:{request.WorkOrderId}";
        var provenance = await dbContext.ProcessedIntegrationEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ConsumerName == WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName &&
                    x.EventType == ErpIntegrationEventTypes.WorkOrderCostCapitalized &&
                    x.EventVersion == ErpIntegrationEventVersions.V1 &&
                    x.SourceService == ErpIntegrationEventSources.BusinessErp &&
                    x.IdempotencyKey == capitalizationIdempotencyKey,
                cancellationToken);
        if (provenance is null)
        {
            return Pending("erp-capitalization-provenance-not-observed");
        }

        if (workOrder.CapitalizedUnitCost is not > 0m)
        {
            return Pending("capitalized-unit-cost-not-ready");
        }

        if (receipt.UnitCost is not null && receipt.UnitCost != workOrder.CapitalizedUnitCost)
        {
            return Rejected("receipt-cost-conflict");
        }

        return new MesFinishedGoodsReceiptCostAuthorityResponse(
            MesFinishedGoodsCostAuthorityStatuses.Available,
            workOrder.CapitalizedUnitCost,
            provenance.EventId);
    }

    private static MesFinishedGoodsReceiptCostAuthorityResponse Pending(string reasonCode) =>
        new(MesFinishedGoodsCostAuthorityStatuses.Pending, ReasonCode: reasonCode);

    private static MesFinishedGoodsReceiptCostAuthorityResponse Rejected(string reasonCode) =>
        new(MesFinishedGoodsCostAuthorityStatuses.Rejected, ReasonCode: reasonCode);
}
