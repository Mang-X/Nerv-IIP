using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;

[Tags("Business Console Scheduling")]
[HttpPost("/api/business-console/v1/scheduling/workbench/insertion-preview-jobs")]
[BusinessGatewayOperationId("acceptBusinessConsoleSchedulingInsertionPreviewJob")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData<SchedulingInsertionPreviewJobDetailContract>), StatusCodes.Status202Accepted)]
public sealed class AcceptBusinessConsoleSchedulingInsertionPreviewJobEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<SchedulingInsertionPreviewRequestContract, SchedulingInsertionPreviewJobDetailContract>(
        auth, BusinessGatewayPermissions.SchedulingPlansManage)
{
    protected override int StatusCode => StatusCodes.Status202Accepted;
    protected override string OrganizationId(SchedulingInsertionPreviewRequestContract request) => request.OrganizationId;
    protected override string EnvironmentId(SchedulingInsertionPreviewRequestContract request) => request.EnvironmentId;
    protected override Task<SchedulingInsertionPreviewJobDetailContract> ForwardAsync(
        SchedulingInsertionPreviewRequestContract request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.AcceptInsertionPreviewJobAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Scheduling")]
[HttpGet("/api/business-console/v1/scheduling/workbench/insertion-preview-jobs/{jobId}")]
[BusinessGatewayOperationId("getBusinessConsoleSchedulingInsertionPreviewJob")]
public sealed class GetBusinessConsoleSchedulingInsertionPreviewJobEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessSchedulingClient scheduling,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingInsertionPreviewJobRequest, SchedulingInsertionPreviewJobDetailContract>(
        auth, BusinessGatewayPermissions.SchedulingPlansRead)
{
    protected override string OrganizationId(BusinessConsoleSchedulingInsertionPreviewJobRequest request) => request.OrganizationId;
    protected override string EnvironmentId(BusinessConsoleSchedulingInsertionPreviewJobRequest request) => request.EnvironmentId;
    protected override Task<SchedulingInsertionPreviewJobDetailContract> ForwardAsync(
        BusinessConsoleSchedulingInsertionPreviewJobRequest request, string bearerToken, CancellationToken cancellationToken) =>
        scheduling.GetInsertionPreviewJobAsync(tokenProvider.BearerToken, request, cancellationToken);
}
