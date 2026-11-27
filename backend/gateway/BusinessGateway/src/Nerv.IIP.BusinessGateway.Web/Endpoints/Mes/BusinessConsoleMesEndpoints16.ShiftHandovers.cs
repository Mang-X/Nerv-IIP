using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/shift-handovers")]
[BusinessGatewayOperationId("listBusinessConsoleMesShiftHandovers")]
public sealed class ListBusinessConsoleMesShiftHandoversEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesListRequest, BusinessConsoleMesShiftHandoverListResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversRead)
{
    protected override string OrganizationId(BusinessConsoleMesListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesListRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesShiftHandoverListResponse> ForwardAsync(
        BusinessConsoleMesListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.ListShiftHandoversAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/shift-handovers/{handoverId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesShiftHandover")]
public sealed class GetBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesShiftHandoverDetailRequest, BusinessConsoleMesShiftHandoverDetail>(
        auth,
        BusinessGatewayPermissions.MesHandoversRead)
{
    protected override string OrganizationId(BusinessConsoleMesShiftHandoverDetailRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesShiftHandoverDetailRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesShiftHandoverDetail> ForwardAsync(
        BusinessConsoleMesShiftHandoverDetailRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        mes.GetShiftHandoverAsync(tokenProvider.BearerToken, request.HandoverId, request, cancellationToken);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/shift-handovers")]
[BusinessGatewayOperationId("createBusinessConsoleMesShiftHandover")]
public sealed class CreateBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesCreateShiftHandoverRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleMesCreateShiftHandoverRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesCreateShiftHandoverRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesCreateShiftHandoverRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 交班人绑定到认证 principal：交接班记录的全部意义就是「谁交给谁」的问责，
        // 允许请求体自带 outgoingUserId 就等于允许持 handovers.manage 的人把别人写成交班人。
        var outgoingUserId = RequireAuthorizedPrincipalId();
        var outgoingUserName = await BusinessConsoleMesShiftHandoverPrincipal.ResolveDisplayNameAsync(
            masterData,
            tokenProvider.BearerToken,
            request.OrganizationId,
            request.EnvironmentId,
            outgoingUserId,
            cancellationToken);

        return await mes.CreateShiftHandoverAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleMesCreateShiftHandoverForwardRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.ShiftId,
                request.TeamId,
                request.OpenIssueIds,
                request.IdempotencyKey,
                request.TeamName,
                outgoingUserId,
                outgoingUserName,
                request.WipItems,
                request.UnfinishedWorkOrders,
                request.OpenIssues,
                request.Attachments),
            cancellationToken);
    }
}

/// <summary>
/// 端点级幂等键长度上界（#3324）。本处下游权威：
/// MES 侧 CreateShiftHandoverCommandHandler 走 CodeAllocator，落 mes.code_idempotency_keys.idempotency_key(150)。
/// 共同口径见 BusinessConsoleMesEndpoints.cs 顶部的「#3324 端点级幂等键上界」注释块。
/// </summary>
public sealed class BusinessConsoleMesCreateShiftHandoverRequestValidator
    : Validator<BusinessConsoleMesCreateShiftHandoverRequest>
{
    public BusinessConsoleMesCreateShiftHandoverRequestValidator() =>
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
}

[Tags("Business Console MES")]
[HttpPost("/api/business-console/v1/mes/shift-handovers/{handoverId}/accept")]
[BusinessGatewayOperationId("acceptBusinessConsoleMesShiftHandover")]
public sealed class AcceptBusinessConsoleMesShiftHandoverEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMesAcceptShiftHandoverRequest, BusinessConsoleAcceptedResponse>(
        auth,
        BusinessGatewayPermissions.MesHandoversManage)
{
    protected override string OrganizationId(BusinessConsoleMesAcceptShiftHandoverRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesAcceptShiftHandoverRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleAcceptedResponse> ForwardAsync(
        BusinessConsoleMesAcceptShiftHandoverRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 接班人同样绑定到认证 principal：接班动作只能代表操作者本人。
        var incomingUserId = RequireAuthorizedPrincipalId();
        var incomingUserName = await BusinessConsoleMesShiftHandoverPrincipal.ResolveDisplayNameAsync(
            masterData,
            tokenProvider.BearerToken,
            request.OrganizationId,
            request.EnvironmentId,
            incomingUserId,
            cancellationToken);

        return await mes.AcceptShiftHandoverAsync(
            tokenProvider.BearerToken,
            request.HandoverId,
            new BusinessConsoleMesAcceptShiftHandoverForwardRequest(
                request.OrganizationId,
                request.EnvironmentId,
                incomingUserId,
                incomingUserName),
            cancellationToken);
    }
}

/// <summary>
/// 交接人显示名快照的解析口径：身份 id 来自认证 principal，姓名来自 MasterData 员工目录
/// （「人」的业务权威是 MasterData Worker，IAM 只负责登录身份）。
/// principal 不是登记在册的员工时（平台管理员、集成账号）姓名留空——身份 id 始终落库，
/// 缺的只是展示用的名字，不因此拒绝交接班。
/// </summary>
internal static class BusinessConsoleMesShiftHandoverPrincipal
{
    public static async Task<string?> ResolveDisplayNameAsync(
        IBusinessMasterDataClient masterData,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        string userId,
        CancellationToken cancellationToken)
    {
        var directory = await masterData.ListWorkersAsync(
            internalBearerToken,
            new BusinessConsoleWorkerDirectoryRequest(
                organizationId,
                environmentId,
                UserId: userId,
                PageIndex: 1,
                PageSize: 1),
            cancellationToken);
        var displayName = directory.Items.FirstOrDefault()?.DisplayName;
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }
}
