using System.Net.Http.Headers;
using Nerv.IIP.Business.MasterData.Web.Application.Auth;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;

/// <summary>审批部门查询所属的组织和环境。</summary>
public sealed record ApprovalPrincipalDepartmentRequest(string OrganizationId, string EnvironmentId);

public sealed class ApprovalPrincipalDepartmentEndpoint(ISender sender)
    : MasterDataEndpoint<ApprovalPrincipalDepartmentRequest, ResponseData<ApprovalPrincipalDepartmentResponse>>
{
    public override void Configure() =>
        ConfigureMasterDataContract(MasterDataEndpointContracts.Get<ApprovalPrincipalDepartmentEndpoint>());

    public override async Task HandleAsync(ApprovalPrincipalDepartmentRequest req, CancellationToken ct)
    {
        var caller = User.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == ApprovalDepartmentAuthorization.Scheme);
        var organizationId = caller?.FindFirst(ScopedCallerClaimTypes.OrganizationId)?.Value;
        var environmentId = caller?.FindFirst(ScopedCallerClaimTypes.EnvironmentId)?.Value;
        if (caller is null || !caller.HasClaim(ScopedCallerClaimTypes.Subject, "business-approval")
            || organizationId is null || environmentId is null
            || organizationId != req.OrganizationId || environmentId != req.EnvironmentId)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var values = HttpContext.Request.Headers[ApprovalDepartmentUserAuthorization.HeaderName];
        if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var bearer)
            || !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(bearer.Parameter))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var authorization = Resolve<ApprovalDepartmentAuthorization>();
        var (status, userId) = await authorization.VerifyUserAsync(bearer, organizationId, environmentId, ct);
        if (userId is null)
        {
            await Send.StatusCodeAsync(status, ct);
            return;
        }

        var response = await sender.Send(
            new GetApprovalPrincipalDepartmentQuery(organizationId, environmentId, userId), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
