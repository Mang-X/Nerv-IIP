namespace Nerv.IIP.Business.ProductEngineering.Web.Application;

internal static class EngineeringChangeVersionKind
{
    public static string Normalize(string value)
    {
        return value.Trim().Replace('_', '-').ToLowerInvariant() switch
        {
            "engineeringbom" => "engineering-bom",
            "manufacturingbom" => "manufacturing-bom",
            "productionversion" => "production-version",
            "engineeringdocument" => "engineering-document",
            var normalized => normalized,
        };
    }
}
