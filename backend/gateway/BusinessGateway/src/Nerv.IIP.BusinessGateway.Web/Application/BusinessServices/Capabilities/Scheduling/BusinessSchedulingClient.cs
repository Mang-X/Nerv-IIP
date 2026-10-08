using System.Globalization;
using FastEndpoints;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;


public sealed record BusinessConsoleSaveSchedulingWorkingDraftRequest([property: RouteParam] string PlanId,
    string OrganizationId, string EnvironmentId, SchedulingWorkingDraftStateContract State);
public sealed record BusinessConsoleListSchedulingWorkingDraftsRequest([property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId, [property: QueryParam] string? PlanId = null);
public sealed record BusinessConsoleClearSchedulingWorkingDraftRequest([property: RouteParam] string PlanId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleSchedulingInsertionPreviewJobRequest(
    [property: RouteParam] Guid JobId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public interface IBusinessSchedulingClient
{
    Task<SchedulingCandidateSetContract> PreviewCandidatesAsync(string token, SchedulingCandidatePreviewRequestContract request, CancellationToken ct);
    Task<SchedulingCandidateSelectionContract> SelectCandidateAsync(string token, SchedulingCandidateSelectRequestContract request, string userId, CancellationToken ct);

    Task<SchedulingFirstPlanJobContract> AcceptFirstPlanJobAsync(
        string internalBearerToken, SchedulingFirstPlanInputContract input, CancellationToken cancellationToken);

    Task<SchedulingFirstPlanJobContract> GetFirstPlanJobAsync(
        string internalBearerToken, BusinessConsoleSchedulingFirstPlanJobRequest request, CancellationToken cancellationToken);

    Task<SchedulingInsertionPreviewJobDetailContract> AcceptInsertionPreviewJobAsync(
        string internalBearerToken, SchedulingInsertionPreviewRequestContract input, CancellationToken cancellationToken);

    Task<SchedulingInsertionPreviewJobDetailContract> GetInsertionPreviewJobAsync(
        string internalBearerToken, BusinessConsoleSchedulingInsertionPreviewJobRequest request, CancellationToken cancellationToken);

    Task<SchedulingWorkingDraftContract> SaveWorkingDraftAsync(
        string internalBearerToken,
        BusinessConsoleSaveSchedulingWorkingDraftRequest request,
        string userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SchedulingWorkingDraftContract>> ListWorkingDraftsAsync(
        string internalBearerToken,
        BusinessConsoleListSchedulingWorkingDraftsRequest request,
        string userId,
        CancellationToken cancellationToken);

    Task ClearWorkingDraftAsync(
        string internalBearerToken,
        BusinessConsoleClearSchedulingWorkingDraftRequest request,
        string userId,
        CancellationToken cancellationToken);

    Task<SchedulePlanContract> PreviewWorkbenchPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulingWorkbenchPlanRequest request,
        CancellationToken cancellationToken);

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

    Task<BusinessConsoleSchedulingHistoryResponse> ListPlanHistoryAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingHistoryRequest request,
        CancellationToken cancellationToken);

    Task<SchedulePlanContract> GetPlanAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken);

    Task<byte[]> ExportPlanCsvAsync(
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

    Task<IReadOnlyCollection<BusinessConsoleScheduleOperationOverrideResponse>> GetPlanOverridesAsync(
        string internalBearerToken, BusinessConsoleSchedulingPlanRequest request, CancellationToken cancellationToken);

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
    public Task<SchedulingCandidateSetContract> PreviewCandidatesAsync(string token, SchedulingCandidatePreviewRequestContract request, CancellationToken ct) =>
        SendAsync<SchedulingCandidateSetContract>(token, HttpMethod.Post, "/api/business/v1/scheduling/workbench/candidates/preview", request, ct, SchedulingJson.Options);
    public Task<SchedulingCandidateSelectionContract> SelectCandidateAsync(string token, SchedulingCandidateSelectRequestContract request, string userId, CancellationToken ct) =>
        SendAsync<SchedulingCandidateSelectionContract>(token, HttpMethod.Post, "/api/business/v1/scheduling/workbench/candidates/select", request, ct, SchedulingJson.Options,
            message => message.Headers.Add(SchedulingWorkingDraftHeaders.UserId, userId));

    public Task<SchedulingFirstPlanJobContract> AcceptFirstPlanJobAsync(
        string internalBearerToken, SchedulingFirstPlanInputContract input, CancellationToken cancellationToken) =>
        SendAsync<SchedulingFirstPlanJobContract>(internalBearerToken, HttpMethod.Post,
            "/api/business/v1/scheduling/workbench/first-plan-jobs", input, cancellationToken, SchedulingJson.Options);

    public Task<SchedulingFirstPlanJobContract> GetFirstPlanJobAsync(
        string internalBearerToken, BusinessConsoleSchedulingFirstPlanJobRequest request, CancellationToken cancellationToken) =>
        SendAsync<SchedulingFirstPlanJobContract>(internalBearerToken, HttpMethod.Get,
            $"/api/business/v1/scheduling/workbench/first-plan-jobs/{request.JobId}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null, cancellationToken, SchedulingJson.Options);

    public Task<SchedulingInsertionPreviewJobDetailContract> AcceptInsertionPreviewJobAsync(
        string internalBearerToken, SchedulingInsertionPreviewRequestContract input, CancellationToken cancellationToken) =>
        SendAsync<SchedulingInsertionPreviewJobDetailContract>(internalBearerToken, HttpMethod.Post,
            "/api/business/v1/scheduling/workbench/insertion-preview-jobs", input, cancellationToken, SchedulingJson.Options);

    public Task<SchedulingInsertionPreviewJobDetailContract> GetInsertionPreviewJobAsync(
        string internalBearerToken, BusinessConsoleSchedulingInsertionPreviewJobRequest request, CancellationToken cancellationToken) =>
        SendAsync<SchedulingInsertionPreviewJobDetailContract>(internalBearerToken, HttpMethod.Get,
            $"/api/business/v1/scheduling/workbench/insertion-preview-jobs/{request.JobId}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null, cancellationToken, SchedulingJson.Options);

    public Task<SchedulingWorkingDraftContract> SaveWorkingDraftAsync(string token, BusinessConsoleSaveSchedulingWorkingDraftRequest request, string userId, CancellationToken ct) =>
        SendAsync<SchedulingWorkingDraftContract>(token, HttpMethod.Put,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/working-draft", request, ct, SchedulingJson.Options,
            message => message.Headers.Add(SchedulingWorkingDraftHeaders.UserId, userId));

    public Task<IReadOnlyList<SchedulingWorkingDraftContract>> ListWorkingDraftsAsync(string token, BusinessConsoleListSchedulingWorkingDraftsRequest request, string userId, CancellationToken ct) =>
        SendAsync<IReadOnlyList<SchedulingWorkingDraftContract>>(token, HttpMethod.Get,
            "/api/business/v1/scheduling/working-drafts?" + Query(("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId), ("planId", request.PlanId)),
            null, ct, SchedulingJson.Options, message => message.Headers.Add(SchedulingWorkingDraftHeaders.UserId, userId));

    public async Task ClearWorkingDraftAsync(string token, BusinessConsoleClearSchedulingWorkingDraftRequest request, string userId, CancellationToken ct) =>
        _ = await SendAsync<NetCorePal.Extensions.Dto.ResponseData>(token, HttpMethod.Delete,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/working-draft?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null, ct, SchedulingJson.Options, message => message.Headers.Add(SchedulingWorkingDraftHeaders.UserId, userId));

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

    public Task<SchedulePlanContract> PreviewWorkbenchPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateSchedulingWorkbenchPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SchedulePlanContract>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/scheduling/workbench/plans/preview",
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

    public Task<BusinessConsoleSchedulingHistoryResponse> ListPlanHistoryAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingHistoryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleSchedulingHistoryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/scheduling/plans/history?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("pageIndex", request.PageIndex.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", request.PageSize.ToString(CultureInfo.InvariantCulture)),
                ("status", request.Status is { } status ? System.Text.Json.JsonSerializer.SerializeToElement(status, SchedulingJson.Options).GetString() : null),
                ("releasedOn", request.ReleasedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("isInvalidated", request.IsInvalidated?.ToString().ToLowerInvariant())),
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

    public Task<byte[]> ExportPlanCsvAsync(
        string internalBearerToken,
        BusinessConsoleSchedulingPlanRequest request,
        CancellationToken cancellationToken) =>
        SendBytesAsync(
            internalBearerToken,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/csv?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            cancellationToken);

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

    public Task<IReadOnlyCollection<BusinessConsoleScheduleOperationOverrideResponse>> GetPlanOverridesAsync(
        string internalBearerToken, BusinessConsoleSchedulingPlanRequest request, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<BusinessConsoleScheduleOperationOverrideResponse>>(
            internalBearerToken, HttpMethod.Get,
            $"/api/business/v1/scheduling/plans/{Uri.EscapeDataString(request.PlanId)}/overrides?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null, cancellationToken, SchedulingJson.Options);

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
