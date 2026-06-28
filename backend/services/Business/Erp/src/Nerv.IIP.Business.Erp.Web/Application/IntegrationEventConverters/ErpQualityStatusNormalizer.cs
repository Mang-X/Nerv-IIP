namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

internal static class ErpQualityStatusNormalizer
{
    public static string NormalizeReceiptQualityStatus(string qualityStatus)
    {
        var normalized = qualityStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "accepted" or "unrestricted" or "qualified" or "available" => "unrestricted",
            "inspection" or "quality" => "quality",
            "rejected" or "blocked" => "blocked",
            _ => normalized,
        };
    }

    public static bool IsPayableReceiptQuality(string qualityStatus)
    {
        return NormalizeReceiptQualityStatus(qualityStatus) is "unrestricted" or "quality";
    }
}
