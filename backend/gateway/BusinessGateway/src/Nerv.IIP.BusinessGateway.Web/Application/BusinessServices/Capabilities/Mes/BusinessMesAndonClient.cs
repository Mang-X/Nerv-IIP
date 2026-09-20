using Nerv.IIP.BusinessGateway.Web.Application.Auth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMesAndonClient
{
    Task<BusinessConsoleMesAndonCallResponse> RaiseAsync(string token, BusinessConsoleMesRaiseAndonCallRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, CancellationToken ct);
    Task<BusinessConsoleMesAndonCallResponse> ClaimAsync(string token, BusinessConsoleMesAndonCallActionRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, CancellationToken ct);
    Task<BusinessConsoleMesAndonCallResponse> CloseAsync(string token, BusinessConsoleMesAndonCallActionRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, CancellationToken ct);
    Task<BusinessConsoleMesAndonCallResponse> GetAsync(string token, BusinessConsoleMesAndonCallRequest request, PrincipalWorkScopeSelection scope, CancellationToken ct);
    Task<BusinessConsoleMesAndonCallListResponse> ListAsync(string token, BusinessConsoleMesListAndonCallsRequest request, PrincipalWorkScopeSelection scope, CancellationToken ct);
}

public sealed class HttpBusinessMesAndonClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMesAndonClient
{
    private const string Root = "/api/business/v1/mes/andon-calls";

    public Task<BusinessConsoleMesAndonCallResponse> RaiseAsync(string token, BusinessConsoleMesRaiseAndonCallRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, CancellationToken ct) =>
        SendAsync<BusinessConsoleMesAndonCallResponse>(token, HttpMethod.Post, Root,
            new
            {
                request.OrganizationId, request.EnvironmentId, request.IdempotencyKey, request.Category,
                request.WorkOrderId, request.OperationTaskId, request.WorkCenterId,
                AssignedUserIds = Join(scope.AssignedUserIds), TeamIds = Join(scope.TeamIds), WorkCenterIds = Join(scope.WorkCenterIds),
            }, ct, BusinessConsoleMesAndonJson.Options, configureRequest: message => AddAudit(message, audit));

    public Task<BusinessConsoleMesAndonCallResponse> ClaimAsync(string token, BusinessConsoleMesAndonCallActionRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, CancellationToken ct) =>
        ActAsync(token, request, scope, audit, "claim", ct);

    public Task<BusinessConsoleMesAndonCallResponse> CloseAsync(string token, BusinessConsoleMesAndonCallActionRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, CancellationToken ct) =>
        ActAsync(token, request, scope, audit, "close", ct);

    private Task<BusinessConsoleMesAndonCallResponse> ActAsync(string token, BusinessConsoleMesAndonCallActionRequest request, PrincipalWorkScopeSelection scope, BusinessServiceAuditContext audit, string action, CancellationToken ct) =>
        SendAsync<BusinessConsoleMesAndonCallResponse>(token, HttpMethod.Post, $"{Root}/{request.Id:D}/{action}",
            new
            {
                request.OrganizationId, request.EnvironmentId, request.IdempotencyKey,
                AssignedUserIds = Join(scope.AssignedUserIds), TeamIds = Join(scope.TeamIds), WorkCenterIds = Join(scope.WorkCenterIds),
            }, ct, BusinessConsoleMesAndonJson.Options, configureRequest: message => AddAudit(message, audit));

    public Task<BusinessConsoleMesAndonCallResponse> GetAsync(string token, BusinessConsoleMesAndonCallRequest request, PrincipalWorkScopeSelection scope, CancellationToken ct) =>
        SendAsync<BusinessConsoleMesAndonCallResponse>(token, HttpMethod.Get, $"{Root}/{request.Id:D}?" + ScopeQuery(request, scope), null, ct, BusinessConsoleMesAndonJson.Options);

    public Task<BusinessConsoleMesAndonCallListResponse> ListAsync(string token, BusinessConsoleMesListAndonCallsRequest request, PrincipalWorkScopeSelection scope, CancellationToken ct) =>
        SendAsync<BusinessConsoleMesAndonCallListResponse>(token, HttpMethod.Get, Root + "?" + JoinQuery(
            ScopeQuery(request, scope),
            Query(("queue", request.Queue), ("category", request.Category), ("workCenterId", request.WorkCenterId), ("skip", request.Skip), ("take", request.Take))), null, ct, BusinessConsoleMesAndonJson.Options);

    private static string ScopeQuery(IBusinessConsoleMesAndonScopeRequest request, PrincipalWorkScopeSelection scope) =>
        Query(("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId),
            ("assignedUserIds", Join(scope.AssignedUserIds)), ("teamIds", Join(scope.TeamIds)), ("workCenterIds", Join(scope.WorkCenterIds)));

    private static string? Join(IReadOnlyCollection<string> ids) => ids.Count == 0 ? null : string.Join(',', ids.Order(StringComparer.Ordinal));

    private static void AddAudit(HttpRequestMessage message, BusinessServiceAuditContext audit)
    {
        message.Headers.TryAddWithoutValidation("X-Authenticated-Actor", audit.Actor);
        message.Headers.TryAddWithoutValidation("X-Correlation-Id", audit.CorrelationId);
        message.Headers.TryAddWithoutValidation("X-Causation-Id", audit.CausationId);
    }
}
