using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;

public static class BarcodeLabelPermissionCodes
{
    public const string TemplatesManage = NervIipPermissionCodes.BarcodeTemplatesManage;
    public const string Print = NervIipPermissionCodes.BarcodePrint;
    public const string ScansWrite = NervIipPermissionCodes.BarcodeScansWrite;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TemplatesManage,
        Print,
        ScansWrite,
    };
}
