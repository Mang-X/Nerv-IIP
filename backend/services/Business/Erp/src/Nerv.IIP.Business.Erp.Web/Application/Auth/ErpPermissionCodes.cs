using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Erp.Web.Application.Auth;

public static class ErpPermissionCodes
{
    public const string ProcurementRead = NervIipPermissionCodes.ErpProcurementRead;
    public const string ProcurementManage = NervIipPermissionCodes.ErpProcurementManage;
    public const string SalesRead = NervIipPermissionCodes.ErpSalesRead;
    public const string SalesManage = NervIipPermissionCodes.ErpSalesManage;
    public const string FinanceRead = NervIipPermissionCodes.ErpFinanceRead;
    public const string FinanceManage = NervIipPermissionCodes.ErpFinanceManage;
}
