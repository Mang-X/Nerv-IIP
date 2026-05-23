namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

public static class ApprovalPermissionCodes
{
    public const string Read = "business.approvals.read";
    public const string Manage = "business.approvals.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        Read,
        Manage,
    ];
}
