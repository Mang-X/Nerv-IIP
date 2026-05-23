namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;

public static class BarcodeLabelPermissionCodes
{
    public const string TemplatesManage = "business.barcodes.templates.manage";
    public const string Print = "business.barcodes.print";
    public const string ScansWrite = "business.barcodes.scans.write";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TemplatesManage,
        Print,
        ScansWrite,
    };
}
