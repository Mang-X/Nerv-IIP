using Nerv.IIP.Iam.Web.Application.Roles;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Roles;

public sealed record PatchRolePermissionsCommand(
    string RoleId,
    IReadOnlyList<string> PermissionCodes,
    SecurityAuditContext? AuditContext) : ICommand<RoleResponse>;

public sealed class PatchRolePermissionsCommandHandler(IIamRoleApplicationService roles)
    : ICommandHandler<PatchRolePermissionsCommand, RoleResponse>
{
    public async Task<RoleResponse> Handle(PatchRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        return await roles.PatchRolePermissionsAsync(
            request.RoleId,
            request.PermissionCodes,
            request.AuditContext,
            cancellationToken);
    }
}
