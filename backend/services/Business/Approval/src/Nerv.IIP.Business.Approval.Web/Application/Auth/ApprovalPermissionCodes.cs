using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

public static class ApprovalPermissionCodes
{
    public const string Read = NervIipPermissionCodes.ApprovalsRead;
    public const string Manage = NervIipPermissionCodes.ApprovalsManage;

    public static readonly IReadOnlyCollection<string> All =
    [
        Read,
        Manage,
    ];
}
