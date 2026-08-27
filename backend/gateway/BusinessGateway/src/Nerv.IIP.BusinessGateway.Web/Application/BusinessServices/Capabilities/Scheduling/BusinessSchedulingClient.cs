using System.Globalization;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;


public interface IBusinessSchedulingClient
{
    Task<SchedulePlanContract> CreateWorkbenchPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulingWorkbenchPlanRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SchedulePlanContract>(new NotSupportedException());

    Task<SchedulePlanRevisionContract> CreatePlanRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulePlanRevisionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<SchedulePlanRevisionContract>(new NotSupportedException());

    Task<SchedulePlanContract> PreviewPlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);

    Task<SchedulePlanContract> CreatePlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingContextRequest request,
        CancellationToken cancellationToken);

    Task<SchedulePlanContract> GetPlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<GanttScheduleItemContract>> GetPlanGanttAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReleaseSchedulePlanResponse> ReleasePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRevokeSchedulePlanResponse> RevokePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleScheduleOperationOverrideResponse> UpsertOperationOverrideAsync(
        string internalBearerToken,
        BusinessConsoleScheduleOperationOverrideRequest request,
        string actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<OrderUrgencyContract>> ListOrderUrgenciesAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyListRequest request,
        CancellationToken cancellationToken);

    Task<OrderUrgencyDetailContract> GetOrderUrgencyAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyRequest request,
        CancellationToken cancellationToken);

    Task<OrderUrgencyDetailContract> SetOrderUrgencyBusinessPriorityAsync(
        string internalBearerToken,
        BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request,
        string actor,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessSchedulingClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessSchedulingClient
{
    public Task<SchedulePlanContract> CreateWorkbenchPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulingWorkbenchPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/workbench/plans",
            request,
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanRevisionContract> CreatePlanRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulePlanRevisionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanRevisionContract>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/revisions",
            request,
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanContract> PreviewPlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/plans/preview",
            new SchedulingProblemRequest(problem),
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanContract> CreatePlanAsync(
        string internalBearerToken,
        SchedulingProblemContract problem,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/plans",
            new SchedulingProblemRequest(problem),
            cancellationToken,
            SchedulingJson.Options);

    public Task<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<BusinessConsoleSchedulePlanSummaryResponse>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/scheduling/plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("pageIndex", request.PageIndex?.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", request.PageSize?.ToString(CultureInfo.InvariantCulture))),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<SchedulePlanContract> GetPlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<IReadOnlyCollection<GanttScheduleItemContract>> GetPlanGanttAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<GanttScheduleItemContract>>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/gantt?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<BusinessConsoleReleaseSchedulePlanResponse> ReleasePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReleaseSchedulePlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/release?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<BusinessConsoleRevokeSchedulePlanResponse> RevokePlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRevokeSchedulePlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/revoke?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<BusinessConsoleScheduleOperationOverrideResponse> UpsertOperationOverrideAsync(
        string internalBearerToken,
        BusinessConsoleScheduleOperationOverrideRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleScheduleOperationOverrideResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/operations/{Uri.EscapeDataString(request.OperationId)}/override",
            request,
            cancellationToken,
            SchedulingJson.Options,
            message => message.Headers.TryAddWithoutValidation("X-Actor", actor));

    public Task<IReadOnlyCollection<OrderUrgencyContract>> ListOrderUrgenciesAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<OrderUrgencyContract>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/scheduling/order-urgencies?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("orderReferences", request.OrderReferences)),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<OrderUrgencyDetailContract> GetOrderUrgencyAsync(
        string internalBearerToken,
        BusinessConsoleOrderUrgencyRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<OrderUrgencyDetailContract>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/scheduling/order-urgencies/{Uri.EscapeDataString(request.OrderReference)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            SchedulingJson.Options);

    public Task<OrderUrgencyDetailContract> SetOrderUrgencyBusinessPriorityAsync(
        string internalBearerToken,
        BusinessConsoleSetOrderUrgencyBusinessPriorityRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<OrderUrgencyDetailContract>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/scheduling/order-urgencies/{Uri.EscapeDataString(request.OrderReference)}/business-priority",
            new SetOrderUrgencyBusinessPriorityForwardRequest(
                request.OrderReference, request.OrganizationId, request.EnvironmentId,
                request.Level, request.Reason, request.ExpiresAtUtc),
            cancellationToken,
            SchedulingJson.Options,
            message => message.Headers.TryAddWithoutValidation("X-Actor", actor));

    private sealed record SchedulingProblemRequest(SchedulingProblemContract Problem);
    private sealed record SetOrderUrgencyBusinessPriorityForwardRequest(
        string OrderReference,
        string OrganizationId,
        string EnvironmentId,
        string Level,
        string Reason,
        DateTimeOffset? ExpiresAtUtc);

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));
}
