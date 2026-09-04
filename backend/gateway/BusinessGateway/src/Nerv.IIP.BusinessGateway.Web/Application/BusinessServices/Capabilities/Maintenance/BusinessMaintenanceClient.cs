using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMaintenanceClient
{
    Task<BusinessConsoleMaintenanceReasonDirectoryResponse> ListDowntimeReasonsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceReasonDirectoryRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Maintenance reason directory client is not configured.");

    Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> CreateWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateMaintenanceWorkOrderV2Response> CreateWorkOrderV2Async(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderV2Request request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Maintenance v2 work-order client is not configured.");

    Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderItem> GetWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderActionResponse> AssignWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderActionResponse?> ProbeAssignmentReplayAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceWorkOrderActionResponse> TransitionWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleTransitionMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenancePlanListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateMaintenancePlanResponse> CreatePlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenancePlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleUpdateMaintenancePlanResponse> UpdatePlanAsync(
        string internalBearerToken,
        string planId,
        BusinessConsoleUpdateMaintenancePlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse> GenerateDueWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordMaintenanceInspectionResponse> RecordInspectionAsync(
        string internalBearerToken,
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceInspectionListResponse> ListInspectionsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse> QueryInspectionMeasurementTrendAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceSparePartListResponse> ListSparePartsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateMaintenanceSparePartResponse> CreateSparePartAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceSparePartRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeAvailabilityResponse> GetAvailabilityWindowsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeAvailabilityResponse> GetAssetAvailabilityWindowsAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAssetReliabilityResponse> QueryAssetReliabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleQueryMaintenanceAssetReliabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleMaintenanceReliabilitySummaryResponse> QueryReliabilitySummaryAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessMaintenanceClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessMaintenanceClient
{
    private static readonly JsonSerializerOptions LifecycleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public async Task<BusinessConsoleMaintenanceReasonDirectoryResponse> ListDowntimeReasonsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceReasonDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamDowntimeReasonDirectoryItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/downtime-reasons?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);

        if (response.Items is null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleMaintenanceReasonDirectoryResponse(
            response.Items.Select(item => new BusinessConsoleMaintenanceReasonDirectoryItem(
                item.ReasonCode,
                item.ReasonCode,
                item.Description,
                item.ReasonCategory,
                item.LossCategory)).ToArray(),
            response.Skip,
            response.Take,
            response.Total);
    }

    public async Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> CreateWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (resourceId, receipt) = await SendCreateWorkOrderAsync(
            internalBearerToken,
            "/api/business/v1/maintenance/work-orders",
            request,
            request.IdempotencyKey,
            cancellationToken);
        return new BusinessConsoleCreateMaintenanceWorkOrderResponse(resourceId, receipt);
    }

    public async Task<BusinessConsoleCreateMaintenanceWorkOrderV2Response> CreateWorkOrderV2Async(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceWorkOrderV2Request request,
        CancellationToken cancellationToken)
    {
        var (resourceId, receipt) = await SendCreateWorkOrderAsync(
            internalBearerToken,
            "/api/business/v2/maintenance/work-orders",
            request,
            request.IdempotencyKey,
            cancellationToken);
        return new BusinessConsoleCreateMaintenanceWorkOrderV2Response(resourceId, receipt);
    }

    // v1 与 v2 共享同一段下游响应校验和回执构造：任何一处收紧/放松都同时作用于两个版本，
    // 避免 v2 复制出一份弱化校验（#2969）。
    private async Task<(string ResourceId, BusinessConsoleOperationReceipt? Receipt)> SendCreateWorkOrderAsync(
        string internalBearerToken,
        string downstreamPath,
        object payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateMaintenanceWorkOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            downstreamPath,
            payload,
            cancellationToken);
        var resourceId = FormatMaintenanceWorkOrderId(response.WorkOrderId);
        if (!Guid.TryParse(resourceId, out var parsedWorkOrderId)
            || parsedWorkOrderId == Guid.Empty
            || !string.Equals(response.Status, "Open", StringComparison.Ordinal)
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return (
            resourceId,
            string.IsNullOrWhiteSpace(idempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    "maintenance.work-order.create",
                    "maintenance",
                    "maintenance-work-order",
                    resourceId,
                    response.ChangedAtUtc,
                    response.Status,
                    idempotencyKey));
    }

    public async Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> CompleteWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCompleteMaintenanceWorkOrderResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}/complete",
            new DownstreamCompleteMaintenanceWorkOrderRequest(
                new DownstreamMaintenanceWorkOrderId(workOrderId),
                request.OrganizationId,
                request.EnvironmentId,
                request.Result,
                request.DowntimeReasonCode,
                request.DowntimeMinutes,
                request.SpareParts,
                request.ActualLaborMinutes,
                request.SparePartCostAmount,
                request.ExternalServiceCostAmount,
                request.CostCurrencyCode,
                request.ActualTechnicianUserId,
                request.IdempotencyKey),
            cancellationToken);
        var responseWorkOrderId = FormatMaintenanceWorkOrderId(response.WorkOrderId);
        if (!Guid.TryParse(responseWorkOrderId, out var parsedWorkOrderId)
            || parsedWorkOrderId == Guid.Empty
            || !Guid.TryParse(workOrderId, out var requestedWorkOrderId)
            || requestedWorkOrderId != parsedWorkOrderId
            || !string.Equals(response.Status, "Completed", StringComparison.Ordinal)
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleCompleteMaintenanceWorkOrderResponse(
            true,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    "maintenance.work-order.complete",
                    "maintenance",
                    "maintenance-work-order",
                    responseWorkOrderId,
                    response.ChangedAtUtc,
                    response.Status,
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        CancellationToken cancellationToken)
    {
        DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem> workOrders;
        if (request.DeviceAssetReferences is { Length: > 0 })
        {
            workOrders = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem>>(
                internalBearerToken,
                HttpMethod.Post,
                "/api/business/internal/v1/maintenance/work-orders/query",
                new DownstreamListMaintenanceWorkOrdersRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.Skip,
                    request.Take,
                    request.DeviceAssetIds,
                    request.Status,
                    request.DeviceAssetId,
                    request.Keyword,
                    request.AssignedTechnicianUserIds,
                    request.AssignedTeamIds,
                    request.WorkOrderId,
                    request.DeviceAssetReferences),
                cancellationToken);
        }
        else
        {
            var scalarQuery = Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("deviceAssetIds", request.DeviceAssetIds),
                ("status", request.Status),
                ("deviceAssetId", request.DeviceAssetId),
                ("keyword", request.Keyword),
                ("assignedTechnicianUserIds", request.AssignedTechnicianUserIds),
                ("assignedTeamIds", request.AssignedTeamIds),
                ("workOrderId", request.WorkOrderId));
            workOrders = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceWorkOrderListItem>>(
                internalBearerToken,
                HttpMethod.Get,
                "/api/business/v1/maintenance/work-orders?" + scalarQuery,
                null,
                cancellationToken);
        }
        return new BusinessConsoleMaintenanceWorkOrderListResponse(workOrders.Items.Select(workOrder =>
            new BusinessConsoleMaintenanceWorkOrderItem(
                FormatMaintenanceWorkOrderId(workOrder.WorkOrderId),
                workOrder.DeviceAssetId,
                workOrder.Priority,
                workOrder.Status,
                workOrder.SourceAlarmId,
                null,
                workOrder.OpenedAtUtc,
                workOrder.AssignedTechnicianUserId,
                workOrder.EstimatedLaborMinutes,
                workOrder.ActualLaborMinutes,
                workOrder.SparePartCostAmount,
                workOrder.ExternalServiceCostAmount,
                workOrder.CostCurrencyCode,
                ActualTechnicianUserId: workOrder.ActualTechnicianUserId,
                SourceReferenceId: workOrder.SourceReferenceId,
                AssignedTeamId: workOrder.AssignedTeamId,
                Version: workOrder.Version)).ToArray(),
            workOrders.Skip,
            workOrders.Take,
            workOrders.Total);
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderItem> GetWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleMaintenanceContextRequest request,
        CancellationToken cancellationToken)
    {
        var detail = await SendAsync<DownstreamMaintenanceWorkOrderDetail>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
        EnsureMaintenanceWorkOrderDetailMatchesRoute(workOrderId, detail.WorkOrder.WorkOrderId);
        return MapWorkOrder(detail.WorkOrder) with
        {
            AllowedActions = detail.AllowedActions,
            BlockReasons = detail.BlockReasons ?? [],
            Lifecycle = detail.Lifecycle.Select(x => new BusinessConsoleMaintenanceWorkOrderLifecycleEventItem(
                x.Action, x.FromStatus, x.ToStatus, x.ActorPrincipalId, x.TechnicianUserId, x.TeamId,
                x.Reason, x.ResultingVersion, x.OccurredAtUtc)).ToArray(),
        };
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderActionResponse> AssignWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken)
    {
        var authoritativeBefore = await GetWorkOrderAsync(
            internalBearerToken,
            workOrderId,
            new BusinessConsoleMaintenanceContextRequest(request.OrganizationId, request.EnvironmentId),
            cancellationToken);
        EnsureAuthoritativePreRead(workOrderId, authoritativeBefore);
        return await SendLifecycleActionAsync(
            internalBearerToken,
            workOrderId,
            "/assignment",
            new DownstreamAssignMaintenanceWorkOrderRequest(
                new DownstreamMaintenanceWorkOrderId(workOrderId),
                request.OrganizationId,
                request.EnvironmentId,
                actorPrincipalId,
                request.TechnicianUserId,
                request.TeamId,
                request.Reason,
                request.IdempotencyKey,
                request.ExpectedVersion),
            "maintenance.work-order.assign",
            request.IdempotencyKey,
            "Open",
            request.ExpectedVersion,
            cancellationToken);
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderActionResponse?> ProbeAssignmentReplayAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleAssignMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken)
    {
        var probe = await SendAsync<DownstreamMaintenanceAssignmentReplayProbeResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/internal/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}/assignment-replay-probe",
            new DownstreamProbeMaintenanceWorkOrderAssignmentReplayRequest(
                request.OrganizationId,
                request.EnvironmentId,
                actorPrincipalId,
                request.TechnicianUserId,
                request.TeamId,
                request.Reason,
                request.IdempotencyKey,
                request.ExpectedVersion),
            cancellationToken,
            LifecycleJsonOptions);
        if (!probe.Found)
        {
            if (probe.Receipt is not null)
            {
                throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                    HttpStatusCode.BadGateway,
                    "downstream-invalid-response");
            }
            return null;
        }
        if (probe.Receipt is null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return MapLifecycleActionResponse(
            workOrderId,
            probe.Receipt,
            "maintenance.work-order.assign",
            request.IdempotencyKey,
            "Open",
            request.ExpectedVersion);
    }

    public Task<BusinessConsoleMaintenanceWorkOrderActionResponse> TransitionWorkOrderAsync(
        string internalBearerToken,
        string workOrderId,
        BusinessConsoleTransitionMaintenanceWorkOrderRequest request,
        string actorPrincipalId,
        CancellationToken cancellationToken) =>
        SendLifecycleActionAsync(
            internalBearerToken,
            workOrderId,
            "/actions",
            new DownstreamTransitionMaintenanceWorkOrderRequest(
                new DownstreamMaintenanceWorkOrderId(workOrderId),
                request.OrganizationId,
                request.EnvironmentId,
                request.Action,
                actorPrincipalId,
                request.Reason,
                request.IdempotencyKey,
                request.ExpectedVersion,
                request.Result,
                request.DowntimeReasonCode,
                request.DowntimeMinutes,
                request.SpareParts,
                request.ActualLaborMinutes,
                request.SparePartCostAmount,
                request.ExternalServiceCostAmount,
                request.CostCurrencyCode),
            $"maintenance.work-order.{request.Action.ToString().ToLowerInvariant()}",
            request.IdempotencyKey,
            ExpectedStatus(request.Action),
            request.ExpectedVersion,
            cancellationToken);

    private async Task<BusinessConsoleMaintenanceWorkOrderActionResponse> SendLifecycleActionAsync<TRequest>(
        string internalBearerToken,
        string workOrderId,
        string routeSuffix,
        TRequest request,
        string operationType,
        string idempotencyKey,
        string expectedStatus,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamMaintenanceWorkOrderActionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/maintenance/work-orders/{Uri.EscapeDataString(workOrderId)}{routeSuffix}",
            request,
            cancellationToken,
            LifecycleJsonOptions);
        return MapLifecycleActionResponse(
            workOrderId,
            response,
            operationType,
            idempotencyKey,
            expectedStatus,
            expectedVersion);
    }

    private static BusinessConsoleMaintenanceWorkOrderActionResponse MapLifecycleActionResponse(
        string workOrderId,
        DownstreamMaintenanceWorkOrderActionResponse response,
        string operationType,
        string idempotencyKey,
        string expectedStatus,
        int expectedVersion)
    {
        var responseId = FormatMaintenanceWorkOrderId(response.WorkOrderId);
        if (!Guid.TryParse(responseId, out var parsedResponseId)
            || parsedResponseId == Guid.Empty
            || !Guid.TryParse(workOrderId, out var parsedRequestId)
            || parsedRequestId != parsedResponseId
            || !string.Equals(response.Status, expectedStatus, StringComparison.Ordinal)
            || expectedVersion < 0
            || expectedVersion == int.MaxValue
            || response.Version != expectedVersion + 1
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
        return new BusinessConsoleMaintenanceWorkOrderActionResponse(
            responseId,
            response.Status,
            response.Version,
            response.ChangedAtUtc,
            BusinessConsoleOperationReceipts.Confirmed(
                operationType,
                "maintenance",
                "maintenance-work-order",
                responseId,
                response.ChangedAtUtc,
                response.Status,
                idempotencyKey));
    }

    private static void EnsureAuthoritativePreRead(
        string workOrderId,
        BusinessConsoleMaintenanceWorkOrderItem authoritativeBefore)
    {
        if (!Guid.TryParse(workOrderId, out var requestedId)
            || !Guid.TryParse(authoritativeBefore.WorkOrderId, out var responseId)
            || requestedId != responseId
            || authoritativeBefore.Version < 0
            || !KnownMaintenanceStatuses.Contains(authoritativeBefore.Status))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static void EnsureMaintenanceWorkOrderDetailMatchesRoute(
        string workOrderId,
        JsonElement downstreamWorkOrderId)
    {
        if (!Guid.TryParse(workOrderId, out var requestedId)
            || requestedId == Guid.Empty
            || downstreamWorkOrderId.ValueKind != JsonValueKind.Object
            || !downstreamWorkOrderId.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || !Guid.TryParse(id.GetString(), out var responseId)
            || responseId == Guid.Empty
            || responseId != requestedId)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static string ExpectedStatus(BusinessConsoleMaintenanceWorkOrderAction action) => action switch
    {
        BusinessConsoleMaintenanceWorkOrderAction.Accept => "Accepted",
        BusinessConsoleMaintenanceWorkOrderAction.Start or BusinessConsoleMaintenanceWorkOrderAction.Resume => "InProgress",
        BusinessConsoleMaintenanceWorkOrderAction.Pause => "Paused",
        BusinessConsoleMaintenanceWorkOrderAction.WaitForParts => "WaitingForParts",
        BusinessConsoleMaintenanceWorkOrderAction.Complete => "Completed",
        BusinessConsoleMaintenanceWorkOrderAction.Verify => "Verified",
        BusinessConsoleMaintenanceWorkOrderAction.Close => "Closed",
        BusinessConsoleMaintenanceWorkOrderAction.Cancel => "Cancelled",
        _ => throw BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadGateway,
            "downstream-invalid-response"),
    };

    private static readonly HashSet<string> KnownMaintenanceStatuses =
    [
        "Open", "Accepted", "InProgress", "Paused", "WaitingForParts", "Completed", "Verified", "Closed", "Cancelled",
    ];

    private static BusinessConsoleMaintenanceWorkOrderItem MapWorkOrder(DownstreamMaintenanceWorkOrderListItem workOrder) =>
        new(
            FormatMaintenanceWorkOrderId(workOrder.WorkOrderId),
            workOrder.DeviceAssetId,
            workOrder.Priority,
            workOrder.Status,
            workOrder.SourceAlarmId,
            null,
            workOrder.OpenedAtUtc,
            workOrder.AssignedTechnicianUserId,
            workOrder.EstimatedLaborMinutes,
            workOrder.ActualLaborMinutes,
            workOrder.SparePartCostAmount,
            workOrder.ExternalServiceCostAmount,
            workOrder.CostCurrencyCode,
            ActualTechnicianUserId: workOrder.ActualTechnicianUserId,
            SourceReferenceId: workOrder.SourceReferenceId,
            AssignedTeamId: workOrder.AssignedTeamId,
            Version: workOrder.Version);

    public async Task<BusinessConsoleMaintenancePlanListResponse> ListPlansAsync(
        string internalBearerToken,
        BusinessConsoleMaintenancePlanListRequest request,
        CancellationToken cancellationToken)
    {
        var plans = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenancePlanListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("deviceAssetId", request.DeviceAssetId)),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenancePlanListResponse(plans.Items.Select(plan =>
            new BusinessConsoleMaintenancePlanItem(
                FormatJsonScalar(plan.PlanId),
                plan.DeviceAssetId,
                plan.PlanCode,
                plan.Interval,
                plan.StartsOn,
                plan.NextDueOn,
                plan.RuntimeHourInterval,
                plan.NextDueRuntimeHours,
                plan.LastGeneratedRuntimeHours)).ToArray(),
            plans.Skip,
            plans.Take,
            plans.Total);
    }

    public async Task<BusinessConsoleCreateMaintenancePlanResponse> CreatePlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateMaintenancePlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/plans",
            request,
            cancellationToken);
        return new BusinessConsoleCreateMaintenancePlanResponse(FormatJsonScalar(response.PlanId));
    }

    public async Task<BusinessConsoleUpdateMaintenancePlanResponse> UpdatePlanAsync(
        string internalBearerToken,
        string planId,
        BusinessConsoleUpdateMaintenancePlanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamUpdateMaintenancePlanResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/maintenance/plans/{Uri.EscapeDataString(planId)}",
            request,
            cancellationToken);
        return new BusinessConsoleUpdateMaintenancePlanResponse(FormatJsonScalar(response.PlanId));
    }

    public async Task<BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse> GenerateDueWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleGenerateDueMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        // Downstream Maintenance requires an OpenedBy for the work orders it raises; the
        // console exposes a single RequestedBy actor, so forward it as both fields.
        var downstreamRequest = new
        {
            request.OrganizationId,
            request.EnvironmentId,
            request.BusinessDate,
            request.RequestedBy,
            OpenedBy = request.RequestedBy,
        };
        var response = await SendAsync<DownstreamGenerateDueMaintenanceWorkOrdersResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/plans/generate-due",
            downstreamRequest,
            cancellationToken);
        return new BusinessConsoleGenerateDueMaintenanceWorkOrdersResponse(
            response.GeneratedCount,
            response.WorkOrderIds.Select(FormatJsonScalar).ToArray());
    }

    public async Task<BusinessConsoleRecordMaintenanceInspectionResponse> RecordInspectionAsync(
        string internalBearerToken,
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRecordMaintenanceInspectionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/inspections",
            request,
            cancellationToken);
        return new BusinessConsoleRecordMaintenanceInspectionResponse(FormatJsonScalar(response.InspectionId));
    }

    public async Task<BusinessConsoleMaintenanceInspectionListResponse> ListInspectionsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        var inspections = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceInspectionListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/inspections?" + ListQuery(request.OrganizationId, request.EnvironmentId, request.Skip, request.Take),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceInspectionListResponse(inspections.Items.Select(inspection =>
            new BusinessConsoleMaintenanceInspectionItem(
                FormatJsonScalar(inspection.InspectionId),
                FormatOptionalJsonScalar(inspection.PlanId),
                FormatOptionalJsonScalar(inspection.WorkOrderId),
                inspection.Inspector,
                inspection.Result,
                inspection.InspectedAtUtc,
                inspection.Measurements ?? [])).ToArray(),
            inspections.Skip,
            inspections.Take,
            inspections.Total);
    }

    public async Task<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse> QueryInspectionMeasurementTrendAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request,
        CancellationToken cancellationToken)
    {
        var trend = await SendAsync<DownstreamMaintenanceInspectionMeasurementTrendResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/inspection-measurements/trends?" + InspectionMeasurementTrendQuery(request),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceInspectionMeasurementTrendResponse(
            trend.OrganizationId,
            trend.EnvironmentId,
            trend.DeviceAssetId,
            trend.CharacteristicCode,
            trend.WindowStartUtc,
            trend.WindowEndUtc,
            trend.Items.Select(item => new BusinessConsoleMaintenanceInspectionMeasurementTrendItem(
                FormatJsonScalar(item.InspectionId),
                FormatOptionalJsonScalar(item.PlanId),
                FormatOptionalJsonScalar(item.WorkOrderId),
                item.InspectedAtUtc,
                item.MeasuredValue,
                item.UomCode,
                item.LowerSpecLimit,
                item.UpperSpecLimit,
                item.IsWithinSpec)).ToArray());
    }

    public async Task<BusinessConsoleMaintenanceSparePartListResponse> ListSparePartsAsync(
        string internalBearerToken,
        BusinessConsoleMaintenanceListRequest request,
        CancellationToken cancellationToken)
    {
        var spareParts = await SendAsync<DownstreamMaintenancePagedResponse<DownstreamMaintenanceSparePartListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/spare-parts?" + ListQuery(request.OrganizationId, request.EnvironmentId, request.Skip, request.Take),
            null,
            cancellationToken);
        return new BusinessConsoleMaintenanceSparePartListResponse(spareParts.Items.Select(sparePart =>
            new BusinessConsoleMaintenanceSparePartItem(
                FormatJsonScalar(sparePart.SparePartLineId),
                FormatMaintenanceWorkOrderId(sparePart.WorkOrderId),
                sparePart.DeviceAssetId,
                sparePart.SkuCode,
                sparePart.Quantity,
                sparePart.UomCode)).ToArray(),
            spareParts.Skip,
            spareParts.Take,
            spareParts.Total);
    }

    public async Task<BusinessConsoleCreateMaintenanceSparePartResponse> CreateSparePartAsync(
        string internalBearerToken,
        BusinessConsoleCreateMaintenanceSparePartRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateMaintenanceSparePartResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/maintenance/spare-parts",
            request,
            cancellationToken);
        return new BusinessConsoleCreateMaintenanceSparePartResponse(FormatJsonScalar(response.SparePartLineId));
    }

    public Task<EquipmentRuntimeAvailabilityResponse> GetAvailabilityWindowsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/availability-windows?" + AvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<EquipmentRuntimeAvailabilityResponse> GetAssetAvailabilityWindowsAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/maintenance/assets/{Uri.EscapeDataString(deviceAssetId)}/availability-windows?" + DeviceAvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<BusinessConsoleAssetReliabilityResponse> QueryAssetReliabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleQueryMaintenanceAssetReliabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAssetReliabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/maintenance/assets/{Uri.EscapeDataString(deviceAssetId)}/reliability?" + ReliabilityQuery(request),
            null,
            cancellationToken);

    public Task<BusinessConsoleMaintenanceReliabilitySummaryResponse> QueryReliabilitySummaryAsync(
        string internalBearerToken,
        BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleMaintenanceReliabilitySummaryResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/maintenance/reliability/summary?" + ReliabilitySummaryQuery(request),
            null,
            cancellationToken);

    private static string AvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc),
            ("deviceAssetIds", request.DeviceAssetIds),
            ("workCenterIds", request.WorkCenterIds));

    private static string DeviceAvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ReliabilityQuery(BusinessConsoleQueryMaintenanceAssetReliabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ReliabilitySummaryQuery(BusinessConsoleQueryMaintenanceReliabilitySummaryRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc),
            ("deviceAssetId", request.DeviceAssetId),
            ("technicianUserId", request.TechnicianUserId));

    private static string InspectionMeasurementTrendQuery(BusinessConsoleQueryMaintenanceInspectionMeasurementTrendRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("deviceAssetId", request.DeviceAssetId),
            ("characteristicCode", request.CharacteristicCode),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string ListQuery(string organizationId, string environmentId, int skip, int take) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId), ("skip", skip), ("take", take));

    private static string FormatJsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.ToString(),
    };

    private static string FormatMaintenanceWorkOrderId(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? FormatOptionalJsonScalar(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : FormatJsonScalar(value.Value);

    private sealed record DownstreamMaintenancePagedResponse<T>(IReadOnlyCollection<T> Items, int Skip, int Take, int Total);

    private sealed record DownstreamListMaintenanceWorkOrdersRequest(
        string OrganizationId,
        string EnvironmentId,
        int Skip,
        int Take,
        string? DeviceAssetIds,
        string? Status,
        string? DeviceAssetId,
        string? Keyword,
        string? AssignedTechnicianUserIds,
        string? AssignedTeamIds,
        string? WorkOrderId,
        string[] DeviceAssetReferences);

    private sealed record DownstreamDowntimeReasonDirectoryItem(
        JsonElement DowntimeReasonId,
        string OrganizationId,
        string EnvironmentId,
        string ReasonCode,
        string Description,
        string ReasonCategory,
        string LossCategory);

    private sealed record DownstreamMaintenanceWorkOrderListItem(
        JsonElement WorkOrderId,
        string DeviceAssetId,
        string Priority,
        string Status,
        string? SourceAlarmId,
        DateTimeOffset OpenedAtUtc,
        string? AssignedTechnicianUserId = null,
        int? EstimatedLaborMinutes = null,
        int? ActualLaborMinutes = null,
        decimal? SparePartCostAmount = null,
        decimal? ExternalServiceCostAmount = null,
        string? CostCurrencyCode = null,
        string? ActualTechnicianUserId = null,
        string? SourceReferenceId = null,
        string? AssignedTeamId = null,
        int Version = 0);

    private sealed record DownstreamMaintenanceWorkOrderDetail(
        DownstreamMaintenanceWorkOrderListItem WorkOrder,
        IReadOnlyCollection<DownstreamMaintenanceWorkOrderLifecycleEvent> Lifecycle,
        IReadOnlyCollection<string> AllowedActions,
        IReadOnlyCollection<string>? BlockReasons = null);

    private sealed record DownstreamMaintenanceWorkOrderLifecycleEvent(
        string Action,
        string FromStatus,
        string ToStatus,
        string ActorPrincipalId,
        string? TechnicianUserId,
        string? TeamId,
        string Reason,
        int ResultingVersion,
        DateTimeOffset OccurredAtUtc);

    private sealed record DownstreamAssignMaintenanceWorkOrderRequest(
        DownstreamMaintenanceWorkOrderId WorkOrderId,
        string OrganizationId,
        string EnvironmentId,
        string ActorPrincipalId,
        string? TechnicianUserId,
        string? TeamId,
        string Reason,
        string IdempotencyKey,
        int ExpectedVersion);

    private sealed record DownstreamProbeMaintenanceWorkOrderAssignmentReplayRequest(
        string OrganizationId,
        string EnvironmentId,
        string ActorPrincipalId,
        string? TechnicianUserId,
        string? TeamId,
        string Reason,
        string IdempotencyKey,
        int ExpectedVersion);

    private sealed record DownstreamMaintenanceAssignmentReplayProbeResponse(
        bool Found,
        DownstreamMaintenanceWorkOrderActionResponse? Receipt);

    private sealed record DownstreamTransitionMaintenanceWorkOrderRequest(
        DownstreamMaintenanceWorkOrderId WorkOrderId,
        string OrganizationId,
        string EnvironmentId,
        BusinessConsoleMaintenanceWorkOrderAction Action,
        string ActorPrincipalId,
        string Reason,
        string IdempotencyKey,
        int ExpectedVersion,
        string? Result,
        string? DowntimeReasonCode,
        int? DowntimeMinutes,
        IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput>? SpareParts,
        int? ActualLaborMinutes,
        decimal? SparePartCostAmount,
        decimal? ExternalServiceCostAmount,
        string? CostCurrencyCode);

    private sealed record DownstreamMaintenanceWorkOrderActionResponse(
        JsonElement WorkOrderId,
        string Status,
        DateTimeOffset ChangedAtUtc,
        int Version);

    private sealed record DownstreamMaintenancePlanListItem(
        JsonElement PlanId,
        string DeviceAssetId,
        string PlanCode,
        string? Interval,
        DateOnly StartsOn,
        DateOnly? NextDueOn,
        decimal? RuntimeHourInterval,
        decimal? NextDueRuntimeHours,
        decimal LastGeneratedRuntimeHours);

    private sealed record DownstreamMaintenanceInspectionListItem(
        JsonElement InspectionId,
        JsonElement? PlanId,
        JsonElement? WorkOrderId,
        string Inspector,
        string Result,
        DateTimeOffset InspectedAtUtc,
        IReadOnlyCollection<BusinessConsoleMaintenanceInspectionMeasurementItem>? Measurements = null);

    private sealed record DownstreamMaintenanceSparePartListItem(
        JsonElement SparePartLineId,
        JsonElement WorkOrderId,
        string DeviceAssetId,
        string SkuCode,
        decimal Quantity,
        string? UomCode);

    private sealed record DownstreamCreateMaintenanceWorkOrderResponse(
        JsonElement WorkOrderId,
        string Status,
        DateTimeOffset ChangedAtUtc);

    private sealed record DownstreamCompleteMaintenanceWorkOrderRequest(
        DownstreamMaintenanceWorkOrderId WorkOrderId,
        string OrganizationId,
        string EnvironmentId,
        string Result,
        string DowntimeReasonCode,
        int DowntimeMinutes,
        IReadOnlyCollection<BusinessConsoleMaintenanceSparePartInput> SpareParts,
        int? ActualLaborMinutes = null,
        decimal? SparePartCostAmount = null,
        decimal? ExternalServiceCostAmount = null,
        string? CostCurrencyCode = null,
        string? ActualTechnicianUserId = null,
        string? IdempotencyKey = null);

    private sealed record DownstreamMaintenanceWorkOrderId(string Id);

    private sealed record DownstreamCompleteMaintenanceWorkOrderResponse(
        JsonElement WorkOrderId,
        string Status,
        DateTimeOffset ChangedAtUtc);

    private sealed record DownstreamCreateMaintenancePlanResponse(JsonElement PlanId);

    private sealed record DownstreamUpdateMaintenancePlanResponse(JsonElement PlanId);

    private sealed record DownstreamGenerateDueMaintenanceWorkOrdersResponse(
        int GeneratedCount,
        IReadOnlyCollection<JsonElement> WorkOrderIds);

    private sealed record DownstreamRecordMaintenanceInspectionResponse(JsonElement InspectionId);

    private sealed record DownstreamCreateMaintenanceSparePartResponse(JsonElement SparePartLineId);

    private sealed record DownstreamMaintenanceInspectionMeasurementTrendResponse(
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string CharacteristicCode,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        IReadOnlyCollection<DownstreamMaintenanceInspectionMeasurementTrendItem> Items);

    private sealed record DownstreamMaintenanceInspectionMeasurementTrendItem(
        JsonElement InspectionId,
        JsonElement? PlanId,
        JsonElement? WorkOrderId,
        DateTimeOffset InspectedAtUtc,
        decimal MeasuredValue,
        string UomCode,
        decimal? LowerSpecLimit,
        decimal? UpperSpecLimit,
        bool IsWithinSpec);
}
