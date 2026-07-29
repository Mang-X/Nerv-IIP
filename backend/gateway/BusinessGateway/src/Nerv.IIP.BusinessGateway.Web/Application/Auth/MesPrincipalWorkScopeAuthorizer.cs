using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed class MesPrincipalWorkScopeAuthorizer(
    PrincipalWorkScopeResolver workScopeResolver,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
{
    public async Task EnsureWorkOrderAccessAsync(
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? requestedScopeKind,
        string? requestedScopeId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var scope = await workScopeResolver.ResolveAsync(
            authorization,
            organizationId,
            environmentId,
            permissionCode,
            requestedScopeKind,
            requestedScopeId,
            cancellationToken);
        var response = await mes.ListWorkOrdersAsync(
            tokenProvider.BearerToken,
            new BusinessMesWorkOrderListRequest(
                organizationId,
                environmentId,
                Keyword: workOrderId,
                Take: 500,
                AssignedUserIds: Join(scope.AssignedUserIds),
                TeamIds: Join(scope.TeamIds),
                WorkCenterIds: Join(scope.WorkCenterIds)),
            cancellationToken);
        if (!response.Items.Any(x => string.Equals(x.WorkOrderId, workOrderId, StringComparison.Ordinal)))
        {
            throw Forbidden();
        }
    }

    public async Task EnsureOperationTaskAccessAsync(
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? requestedScopeKind,
        string? requestedScopeId,
        string operationTaskId,
        CancellationToken cancellationToken)
    {
        var scope = await workScopeResolver.ResolveAsync(
            authorization,
            organizationId,
            environmentId,
            permissionCode,
            requestedScopeKind,
            requestedScopeId,
            cancellationToken);
        var response = await mes.ListOperationTasksAsync(
            tokenProvider.BearerToken,
            new BusinessMesOperationTaskListRequest(
                organizationId,
                environmentId,
                Keyword: operationTaskId,
                Take: 500,
                AssignedUserIds: Join(scope.AssignedUserIds),
                TeamIds: Join(scope.TeamIds),
                WorkCenterIds: Join(scope.WorkCenterIds)),
            cancellationToken);
        if (!response.Items.Any(x => string.Equals(x.OperationTaskId, operationTaskId, StringComparison.Ordinal)))
        {
            throw Forbidden();
        }
    }

    private static string? Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(',', values.Order(StringComparer.Ordinal));

    private static BusinessServiceProxyException Forbidden() =>
        new(HttpStatusCode.Forbidden, "work-scope-not-authorized");
}
