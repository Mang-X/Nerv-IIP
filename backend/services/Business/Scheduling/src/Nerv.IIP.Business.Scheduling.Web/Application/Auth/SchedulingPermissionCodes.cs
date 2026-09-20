using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Auth;

public static class SchedulingPermissionCodes
{
    public const string PlansRead = NervIipPermissionCodes.SchedulingPlansRead;
    public const string PlansManage = NervIipPermissionCodes.SchedulingPlansManage;
    public const string PlansRelease = NervIipPermissionCodes.SchedulingPlansRelease;
}
