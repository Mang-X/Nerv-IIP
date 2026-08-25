namespace Nerv.IIP.Business.Wms.Web.Application.Auth;

public static class WmsPermissionCodes
{
    public const string ReceiptsRead = "business.wms.receipts.read";
    public const string ReceiptsManage = "business.wms.receipts.manage";
    public const string ShipmentsRead = "business.wms.shipments.read";
    public const string ShipmentsManage = "business.wms.shipments.manage";
    public const string CountsRead = "business.wms.counts.read";
    public const string InventoryCountsManage = "business.inventory.counts.manage";
    public const string AutomationManage = "business.wms.automation.manage";

    /// <summary>现场作业池与成员资格的写面（作业资格边界的唯一治理入口）。</summary>
    public const string WorkPoolsManage = "business.wms.work-pools.manage";
}
