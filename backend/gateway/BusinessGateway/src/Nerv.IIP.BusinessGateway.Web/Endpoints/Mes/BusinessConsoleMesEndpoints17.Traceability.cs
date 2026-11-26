using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Endpoints;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

/// <summary>
/// MES 追溯读面的公共门面：取到执行追溯图后，按 <c>business.mes.quality.read</c> 分层检验结论。
/// 检验结论节点带出缺陷码与处置结论，按 authorization-matrix 属质量下钻内容（同一份矩阵已有
/// 「工单列表由 Gateway 按权限感知范围裁剪内容」与「工单详情给保留摘要、逐事件时间线另要 quality.read」
/// 两条内容级分层先例）。只持 <c>business.mes.traceability.read</c> 的主体仍拿到整张执行追溯图，
/// 但不含检验结论节点及其边。
/// <para>
/// <see cref="ForwardAsync"/> 是 sealed 的：继承本基类的读面只能实现 <see cref="LoadTraceabilityAsync"/>，
/// 不能各自决定裁不裁剪。这不构成类型层面的封闭——新端点仍可直接继承 AuthorizedBusinessProxyEndpoint
/// 绕开本基类；兜住这一整类的是三个读面 × 两种权限态的门面用例矩阵。
/// </para>
/// </summary>
public abstract class BusinessConsoleMesTraceabilityEndpoint<TRequest>(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIamAccountDirectoryClient iamAccounts,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<TRequest, BusinessConsoleMesTraceabilityResponse>(
        auth,
        BusinessGatewayPermissions.MesTraceabilityRead)
    where TRequest : notnull
{
    protected string InternalBearerToken => tokenProvider.BearerToken;

    protected abstract Task<BusinessConsoleMesTraceabilityResponse> LoadTraceabilityAsync(
        TRequest request,
        CancellationToken cancellationToken);

    protected sealed override async Task<BusinessConsoleMesTraceabilityResponse> ForwardAsync(
        TRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var response = await LoadTraceabilityAsync(request, cancellationToken);
        var inspectionNodeIds = response.Nodes
            .Where(x => string.Equals(x.NodeType, MesTraceabilityNodeTypes.InspectionResult, StringComparison.Ordinal))
            .Select(x => x.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        var namesTask = MesOperatorDisplayNames.ResolveAsync(iamAccounts, InternalBearerToken,
            OrganizationId(request), EnvironmentId(request),
            response.Nodes.Where(node => string.Equals(node.NodeType, "Operator", StringComparison.Ordinal))
                .Select(node => node.NodeId), cancellationToken);
        if (inspectionNodeIds.Count > 0)
        {
            var qualityTask = AuthorizationClient.CheckAsync(
                bearerToken,
                new BusinessGatewayPermissionRequirement(
                    BusinessGatewayPermissions.MesQualityRead,
                    OrganizationId(request),
                    EnvironmentId(request),
                    null,
                    null),
                BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
                cancellationToken);
            await Task.WhenAll(namesTask, qualityTask);
            if (!(await qualityTask).IsAllowed)
            {
                response = response with
                {
                    Nodes = [.. response.Nodes.Where(x => !string.Equals(x.NodeType, MesTraceabilityNodeTypes.InspectionResult, StringComparison.Ordinal))],
                    Edges = [.. response.Edges.Where(x => !inspectionNodeIds.Contains(x.ToNodeId))],
                };
            }
        }

        var names = await namesTask;
        return response with
        {
            Nodes = [.. response.Nodes.Select(node =>
                string.Equals(node.NodeType, "Operator", StringComparison.Ordinal)
                    ? node with { DisplayName = names.GetValueOrDefault(node.NodeId) ?? "姓名不可用" }
                    : node)],
        };
    }
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesWorkOrderTraceability")]
public sealed class GetBusinessConsoleMesWorkOrderTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessIamAccountDirectoryClient iamAccounts,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesTraceabilityEndpoint<BusinessConsoleMesTraceabilityByWorkOrderRequest>(auth, iamAccounts, tokenProvider)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByWorkOrderRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> LoadTraceabilityAsync(
        BusinessConsoleMesTraceabilityByWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        mes.GetWorkOrderTraceabilityAsync(
            InternalBearerToken,
            request.WorkOrderId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/batches/{batchOrSerial}")]
[BusinessGatewayOperationId("getBusinessConsoleMesBatchTraceability")]
public sealed class GetBusinessConsoleMesBatchTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessIamAccountDirectoryClient iamAccounts,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesTraceabilityEndpoint<BusinessConsoleMesTraceabilityByBatchRequest>(auth, iamAccounts, tokenProvider)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByBatchRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByBatchRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> LoadTraceabilityAsync(
        BusinessConsoleMesTraceabilityByBatchRequest request,
        CancellationToken cancellationToken) =>
        mes.GetBatchTraceabilityAsync(
            InternalBearerToken,
            request.BatchOrSerial,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}

[Tags("Business Console MES")]
[HttpGet("/api/business-console/v1/mes/traceability/material-lots/{materialLotId}")]
[BusinessGatewayOperationId("getBusinessConsoleMesMaterialLotTraceability")]
public sealed class GetBusinessConsoleMesMaterialLotTraceabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMesClient mes,
    IBusinessIamAccountDirectoryClient iamAccounts,
    IInternalServiceTokenProvider tokenProvider)
    : BusinessConsoleMesTraceabilityEndpoint<BusinessConsoleMesTraceabilityByMaterialLotRequest>(auth, iamAccounts, tokenProvider)
{
    protected override string OrganizationId(BusinessConsoleMesTraceabilityByMaterialLotRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMesTraceabilityByMaterialLotRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMesTraceabilityResponse> LoadTraceabilityAsync(
        BusinessConsoleMesTraceabilityByMaterialLotRequest request,
        CancellationToken cancellationToken) =>
        mes.GetMaterialLotTraceabilityAsync(
            InternalBearerToken,
            request.MaterialLotId,
            new BusinessConsoleMesContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
}
