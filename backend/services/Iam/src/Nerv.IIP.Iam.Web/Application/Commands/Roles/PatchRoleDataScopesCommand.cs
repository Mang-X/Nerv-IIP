using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Web.Application.DataScopes;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Roles;

public sealed record PatchRoleDataScopesCommand(
    string RoleId,
    IReadOnlyList<DataScopeBindingRequest> DataScopes,
    SecurityAuditContext? AuditContext) : ICommand<DataScopeListResponse>;

public sealed class PatchRoleDataScopesCommandHandler(IIamDataScopeApplicationService dataScopes, IOptions<IamSeedOptions> seed)
    : ICommandHandler<PatchRoleDataScopesCommand, DataScopeListResponse>
{
    public async Task<DataScopeListResponse> Handle(PatchRoleDataScopesCommand request, CancellationToken cancellationToken)
    {
        PlatformAdministratorProtection.EnsureRoleDataScopesNotReduced(
            seed.Value,
            request.RoleId,
            DataScopeApplicationMapping.Normalize(request.DataScopes));
        return await dataScopes.PatchRoleDataScopesAsync(
            request.RoleId,
            request.DataScopes,
            request.AuditContext,
            cancellationToken);
    }
}
