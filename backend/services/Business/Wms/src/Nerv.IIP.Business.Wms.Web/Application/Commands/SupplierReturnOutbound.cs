using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

internal static class SupplierReturnOutbound
{
    public static OutboundOrder Create(
        SupplierReturnRequest supplierReturn, string sourceDocumentId, string qualityStatus,
        DateOnly? productionDate = null, DateOnly? expiryDate = null, bool matchReceiptStockDates = false)
    {
        return OutboundOrder.Create(
            supplierReturn.OrganizationId,
            supplierReturn.EnvironmentId,
            supplierReturn.SupplierReturnNo,
            WmsSourceDocumentTypes.PurchaseReceiptReturn,
            sourceDocumentId,
            supplierReturn.SiteCode,
            [new OutboundOrderLineDraft(
                supplierReturn.InboundOrderLineNo,
                supplierReturn.SkuCode,
                supplierReturn.UomCode,
                supplierReturn.Quantity,
                supplierReturn.LocationCode,
                supplierReturn.LotNo,
                supplierReturn.SerialNo,
                qualityStatus,
                supplierReturn.OwnerType,
                supplierReturn.OwnerId,
                productionDate,
                expiryDate,
                matchReceiptStockDates)]);
    }
}
