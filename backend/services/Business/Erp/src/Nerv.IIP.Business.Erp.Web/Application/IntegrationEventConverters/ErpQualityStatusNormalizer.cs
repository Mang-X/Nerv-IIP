using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

internal static class ErpQualityStatusNormalizer
{
    // 值域与别名表的唯一来源是 Nerv.IIP.Contracts.Erp.ErpReceiptQualityStatuses（#1345）。
    public static string NormalizeReceiptQualityStatus(string qualityStatus)
    {
        return ErpReceiptQualityStatuses.Normalize(qualityStatus);
    }

    public static bool IsPayableReceiptQuality(string qualityStatus)
    {
        return ErpReceiptQualityStatuses.IsPayable(qualityStatus);
    }
}
