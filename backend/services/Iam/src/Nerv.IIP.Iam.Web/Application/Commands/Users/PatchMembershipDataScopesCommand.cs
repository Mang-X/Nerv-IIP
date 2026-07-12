using Nerv.IIP.Iam.Web.Application.DataScopes;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record PatchMembershipDataScopesCommand(
    string UserId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyList<DataScopeBindingRequest> DataScopes,
    SecurityAuditContext? AuditContext) : ICommand<DataScopeListResponse>;

public sealed class PatchMembershipDataScopesCommandHandler(IIamDataScopeApplicationService dataScopes)
    : ICommandHandler<PatchMembershipDataScopesCommand, DataScopeListResponse>
{
    public async Task<DataScopeListResponse> Handle(PatchMembershipDataScopesCommand request, CancellationToken cancellationToken)
    {
        return await dataScopes.PatchMembershipDataScopesAsync(
            request.UserId,
            request.OrganizationId,
            request.EnvironmentId,
            request.DataScopes,
            request.AuditContext,
            cancellationToken);
    }
}
