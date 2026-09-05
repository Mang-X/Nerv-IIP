using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Wms.Web.Application.Auth;

public static class WmsPermissionCodes
{
    public const string ReceiptsRead = NervIipPermissionCodes.WmsReceiptsRead;
    public const string ReceiptsManage = NervIipPermissionCodes.WmsReceiptsManage;
    public const string ShipmentsRead = NervIipPermissionCodes.WmsShipmentsRead;
    public const string ShipmentsManage = NervIipPermissionCodes.WmsShipmentsManage;
    public const string CountsRead = NervIipPermissionCodes.WmsCountsRead;
    public const string InventoryCountsManage = NervIipPermissionCodes.InventoryCountsManage;
    public const string AutomationManage = NervIipPermissionCodes.WmsAutomationManage;

    /// <summary>现场作业池与成员资格的写面（作业资格边界的唯一治理入口）。</summary>
    public const string WorkPoolsManage = NervIipPermissionCodes.WmsWorkPoolsManage;
}
