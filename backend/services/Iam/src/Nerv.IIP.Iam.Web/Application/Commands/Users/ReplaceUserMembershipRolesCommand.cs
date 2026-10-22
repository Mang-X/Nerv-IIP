using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

/// <summary>
/// 组织与环境取自调用者当前会话，而非请求体：管理员只能在自己所处的组织环境里分配成员与角色。
/// </summary>
public sealed record ReplaceUserMembershipRolesCommand(
    string UserId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyList<string> RoleIds,
    SecurityAuditContext AuditContext) : ICommand<UserMembershipResponse>;

public sealed class ReplaceUserMembershipRolesCommandHandler(
    IIamUserApplicationService users,
    IOptions<IamSeedOptions> seed) : ICommandHandler<ReplaceUserMembershipRolesCommand, UserMembershipResponse>
{
    public async Task<UserMembershipResponse> Handle(ReplaceUserMembershipRolesCommand request, CancellationToken cancellationToken)
    {
        PlatformAdministratorProtection.EnsureMembershipKeepsAdministratorRole(
            seed.Value,
            request.UserId,
            request.OrganizationId,
            request.EnvironmentId,
            request.RoleIds);
        return await users.ReplaceMembershipRolesAsync(
            request.UserId,
            request.OrganizationId,
            request.EnvironmentId,
            request.RoleIds,
            request.AuditContext,
            cancellationToken);
    }
}
