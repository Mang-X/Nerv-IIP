using Nerv.IIP.Iam.Web.Application.Roles;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Roles;

public sealed record CreateRoleCommand(string RoleName, IReadOnlyList<string> PermissionCodes) : ICommand<RoleResponse>;

public sealed class CreateRoleCommandHandler(IIamRoleApplicationService roles)
    : ICommandHandler<CreateRoleCommand, RoleResponse>
{
    public async Task<RoleResponse> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        return await roles.CreateRoleAsync(request.RoleName, request.PermissionCodes, cancellationToken);
    }
}
