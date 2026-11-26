using Nerv.IIP.BusinessGateway.Web.Application.Auth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

internal sealed record ApprovalRoutingMembership(string RoleRefs, string? DepartmentRef)
{
    public static async Task<ApprovalRoutingMembership> LoadAsync(
        IBusinessMasterDataClient masterData, string internalToken, string organizationId, string environmentId,
        BusinessGatewayAuthorizationResult authorization, CancellationToken cancellationToken)
    {
        var roles = string.Join(",", authorization.Roles?.Select(role => role.Id) ?? []);
        var department = authorization.PrincipalType is null or "user" && authorization.PrincipalId is { Length: > 0 } userId
            ? (await masterData.GetPrincipalWorkContextAsync(internalToken,
                new BusinessMasterDataPrincipalWorkContextRequest(organizationId, environmentId, userId),
                cancellationToken)).Worker
            : null;
        return new ApprovalRoutingMembership(roles,
            department is { EmploymentStatus: "active" } ? department.DepartmentId : null);
    }
}
