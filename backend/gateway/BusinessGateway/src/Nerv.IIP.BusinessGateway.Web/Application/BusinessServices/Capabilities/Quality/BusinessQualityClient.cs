using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessQualityClient
{
    Task<BusinessConsoleCreateInspectionPlanResponse> CreateInspectionPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionPlanRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ActivateInspectionPlanAsync(
        string internalBearerToken,
        string inspectionPlanId,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionPlanListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityListResponse> ListInspectionRecordsAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionRecordListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateReinspectionResponse> CreateReinspectionAsync(
        string internalBearerToken,
        string inspectionRecordId,
        BusinessConsoleCreateReinspectionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionTaskListResponse> ListInspectionTasksAsync(
        string internalBearerToken,
        BusinessQualityInspectionTaskListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionTaskDetailResponse> GetInspectionTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityInspectionTaskDetailRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionTaskAssignmentResponse> AssignInspectionTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityAssignInspectionTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionTaskAssignmentResponse> ClaimInspectionTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityClaimInspectionTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateInspectionRecordFromTaskResponse> CreateInspectionRecordFromTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityCreateInspectionRecordFromTaskRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityInspectionPlanCharacteristicListResponse> GetInspectionPlanCharacteristicsAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionPlanCharacteristicsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleOpenNcrFromInspectionResponse> OpenNcrFromInspectionAsync(
        string internalBearerToken,
        string inspectionRecordId,
        BusinessConsoleOpenNcrFromInspectionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityNcrListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityNcrDetailResponse> GetNcrAsync(
        string internalBearerToken,
        BusinessConsoleQualityNcrDetailRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleInspectionRecordDetailResponse> GetInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionRecordDetailRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualitySpcControlChartResponse> QuerySpcControlChartAsync(
        string internalBearerToken,
        BusinessConsoleQualitySpcRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityProcessCapabilityResponse> QueryProcessCapabilityAsync(
        string internalBearerToken,
        BusinessConsoleQualityProcessCapabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualitySpcControlChartListResponse> ListSpcControlChartsAsync(
        string internalBearerToken,
        BusinessConsoleQualitySpcControlChartListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityMeasuringDeviceListResponse> ListMeasuringDevicesAsync(
        string internalBearerToken,
        BusinessConsoleQualityMeasuringDeviceListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityCalibrationRecordListResponse> ListCalibrationRecordsAsync(
        string internalBearerToken,
        BusinessConsoleQualityCalibrationRecordListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityCapaListResponse> ListCorrectiveActionsAsync(
        string internalBearerToken,
        BusinessConsoleQualityCapaListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityCapaItem> GetCorrectiveActionAsync(
        string internalBearerToken,
        string correctiveActionId,
        BusinessConsoleQualityCapaDetailRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonListResponse> ListQualityReasonsAsync(
        string internalBearerToken,
        BusinessConsoleQualityReasonListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> GetQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> CreateQualityReasonAsync(
        string internalBearerToken,
        BusinessConsoleCreateQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> UpdateQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleUpdateQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleQualityReasonItem> ArchiveQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleArchiveQualityReasonRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        string actor,
        CancellationToken cancellationToken);
}
public sealed class HttpBusinessQualityClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessQualityClient
{
    public async Task<BusinessConsoleCreateInspectionPlanResponse> CreateInspectionPlanAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionPlanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateInspectionPlanResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/inspection-plans",
            request,
            cancellationToken);
        if (!Guid.TryParse(response.InspectionPlanId, out var inspectionPlanId) || inspectionPlanId == Guid.Empty)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleCreateInspectionPlanResponse(inspectionPlanId.ToString());
    }

    public Task<BusinessConsoleAcceptedResponse> ActivateInspectionPlanAsync(
        string internalBearerToken,
        string inspectionPlanId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(inspectionPlanId, out var parsedInspectionPlanId) || parsedInspectionPlanId == Guid.Empty)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-plans/{Uri.EscapeDataString(inspectionPlanId)}/activate",
            new DownstreamActivateInspectionPlanRequest(parsedInspectionPlanId.ToString()),
            cancellationToken);
    }

    public async Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionPlanListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("category", request.Category),
                ("status", request.Status),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray(),
            response.Total);
    }

    public Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateInspectionRecordResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/inspection-records",
            ToDownstreamRequest(request),
            cancellationToken);

    public Task<BusinessConsoleCreateReinspectionResponse> CreateReinspectionAsync(
        string internalBearerToken,
        string inspectionRecordId,
        BusinessConsoleCreateReinspectionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateReinspectionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-records/{Uri.EscapeDataString(inspectionRecordId)}/reinspections",
            new DownstreamCreateReinspectionRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.ResultLines?.Select(ToDownstreamLine).ToArray(),
                request.DispositionReason,
                request.DispositionAttachmentFileIds,
                request.MeasuringDeviceId),
            cancellationToken);

    public async Task<BusinessConsoleQualityInspectionTaskListResponse> ListInspectionTasksAsync(
        string internalBearerToken,
        BusinessQualityInspectionTaskListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionTaskListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-tasks?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("skuCode", request.SkuCode),
                ("skip", request.Skip),
                ("take", request.Take),
                ("inspectionTaskId", request.InspectionTaskId),
                ("scopeKind", request.ScopeKind),
                ("principalId", request.PrincipalId),
                ("authorizedTeamIds", JoinValues(request.AuthorizedTeamIds)),
                ("sourceType", request.SourceType),
                ("sourceService", request.SourceService),
                ("keyword", request.Keyword),
                ("overdue", request.Overdue)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityInspectionTaskListResponse(
            response.Items.Select(ToInspectionTaskItem).ToArray(),
            response.Total);
    }

    public async Task<BusinessConsoleQualityInspectionTaskDetailResponse> GetInspectionTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityInspectionTaskDetailRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionTaskDetailResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/quality/inspection-tasks/{Uri.EscapeDataString(inspectionTaskId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("scopeKind", request.ScopeKind),
                ("principalId", request.PrincipalId),
                ("authorizedTeamIds", JoinValues(request.AuthorizedTeamIds))),
            null,
            cancellationToken);
        return new BusinessConsoleQualityInspectionTaskDetailResponse(
            ToInspectionTaskItem(response.Task),
            response.PlanCode,
            response.Category,
            response.Characteristics.Select(x => new BusinessConsoleQualityInspectionTaskCharacteristic(
                x.CharacteristicCode,
                x.Name,
                x.Method,
                x.Severity,
                x.IsRequired,
                x.SamplingRule,
                x.CharacteristicType,
                x.NominalValue,
                x.LowerSpecLimit,
                x.UpperSpecLimit,
                x.UnitCode)).ToArray());
    }

    public Task<BusinessConsoleQualityInspectionTaskAssignmentResponse> AssignInspectionTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityAssignInspectionTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityInspectionTaskAssignmentResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-tasks/{Uri.EscapeDataString(inspectionTaskId)}/assignment",
            request,
            cancellationToken);

    public Task<BusinessConsoleQualityInspectionTaskAssignmentResponse> ClaimInspectionTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityClaimInspectionTaskRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityInspectionTaskAssignmentResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-tasks/{Uri.EscapeDataString(inspectionTaskId)}/claim",
            request,
            cancellationToken);

    public async Task<BusinessConsoleCreateInspectionRecordFromTaskResponse> CreateInspectionRecordFromTaskAsync(
        string internalBearerToken,
        string inspectionTaskId,
        BusinessQualityCreateInspectionRecordFromTaskRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateInspectionRecordFromTaskResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-tasks/{Uri.EscapeDataString(inspectionTaskId)}/inspection-record",
            new DownstreamCreateInspectionRecordFromTaskRequest(
                inspectionTaskId,
                request.OrganizationId,
                request.EnvironmentId,
                request.InspectorUserId,
                request.ResultLines?.Select(ToDownstreamLine).ToArray(),
                request.DispositionReason,
                request.DispositionAttachmentFileIds,
                request.IdempotencyKey),
            cancellationToken);
        if (!Guid.TryParse(response.InspectionRecordId, out var inspectionRecordId)
            || inspectionRecordId == Guid.Empty
            || response.Result is not ("passed" or "rejected" or "conditional-release")
            || response.ChangedAtUtc == default)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return new BusinessConsoleCreateInspectionRecordFromTaskResponse(
            response.InspectionRecordId,
            response.Result,
            response.NonconformanceReportId,
            response.NonconformanceReportCode,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Confirmed(
                    "quality.inspection-task.submit",
                    "quality",
                    "inspection-record",
                    response.InspectionRecordId,
                    response.ChangedAtUtc,
                    response.Result,
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleQualityInspectionPlanCharacteristicListResponse> GetInspectionPlanCharacteristicsAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionPlanCharacteristicsRequest request,
        CancellationToken cancellationToken)
    {
        // The Quality inspection-plans list already resolves a single plan (with characteristics)
        // by id via its keyword filter; no dedicated detail endpoint is needed.
        var response = await SendAsync<DownstreamInspectionPlanListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-plans?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.InspectionPlanId),
                ("skip", 0),
                ("take", 1)),
            null,
            cancellationToken);
        var plan = response.Items.FirstOrDefault(x =>
                string.Equals(x.InspectionPlanId, request.InspectionPlanId, StringComparison.OrdinalIgnoreCase))
            ?? response.Items.FirstOrDefault();
        var items = (plan?.Characteristics ?? [])
            .Select(c => new BusinessConsoleInspectionPlanCharacteristicItem(
                c.CharacteristicCode,
                c.Name,
                c.CharacteristicType,
                c.Required,
                c.NominalValue,
                c.LowerSpecLimit,
                c.UpperSpecLimit,
                c.UnitCode))
            .ToArray();
        return new BusinessConsoleQualityInspectionPlanCharacteristicListResponse(
            request.InspectionPlanId,
            plan?.PlanCode,
            plan?.Category,
            plan?.SkuCode,
            items);
    }

    public async Task<BusinessConsoleQualityListResponse> ListInspectionRecordsAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionRecordListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamInspectionRecordListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/inspection-records?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("sourceType", request.SourceType),
                ("result", request.Status),
                ("skuCode", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityListResponse(
            response.Items.Select(ToQualityItem).ToArray(),
            response.Total);
    }

    public async Task<BusinessConsoleOpenNcrFromInspectionResponse> OpenNcrFromInspectionAsync(
        string internalBearerToken,
        string inspectionRecordId,
        BusinessConsoleOpenNcrFromInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamOpenNcrFromInspectionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/inspection-records/{Uri.EscapeDataString(inspectionRecordId)}/failures/ncr",
            new DownstreamOpenNcrFromInspectionRequest(
                inspectionRecordId,
                request.OrganizationId,
                request.EnvironmentId,
                request.DefectReason,
                request.AttachmentFileIds),
            cancellationToken);
        return new BusinessConsoleOpenNcrFromInspectionResponse(FormatJsonScalar(response.NcrId));
    }

    public async Task<BusinessConsoleQualityNcrListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamNcrListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/ncrs?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityNcrListResponse(
            response.Items.Select(ToNcrItem).ToArray(),
            response.Total);
    }

    public async Task<BusinessConsoleQualityNcrDetailResponse> GetNcrAsync(
        string internalBearerToken,
        BusinessConsoleQualityNcrDetailRequest request,
        CancellationToken cancellationToken)
    {
        // 代理真实详情端点，org/env 随查询下传由 Quality 服务端做租户过滤：越权 id 与不存在同为
        // not found（下游业务错误透传），不泄露跨租户数据。
        var response = await SendAsync<DownstreamNcrItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(request.NcrId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);
        return new BusinessConsoleQualityNcrDetailResponse(
            response.NcrId,
            response.NcrCode,
            response.Status,
            response.SkuCode,
            response.SourceType,
            response.SourceDocumentId,
            response.DefectQuantity,
            response.DefectReason,
            response.BatchNo,
            response.SerialNo,
            response.SourceInspectionRecordId,
            response.ReworkWorkOrderCreationStatus,
            response.DispositionType,
            response.DispositionApprovalChainId,
            response.CloseReason,
            response.ReworkWorkOrderId);
    }

    public async Task<BusinessConsoleInspectionRecordDetailResponse> GetInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleQualityInspectionRecordDetailRequest request,
        CancellationToken cancellationToken)
    {
        // 代理真实详情端点；org/env 随查询下传由 Quality 服务端做租户过滤（越权与不存在同为 not found）。
        var record = await SendAsync<DownstreamInspectionRecordDetail>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/quality/inspection-records/{Uri.EscapeDataString(request.InspectionRecordId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);
        return new BusinessConsoleInspectionRecordDetailResponse(
            record.InspectionRecordId,
            record.SourceType,
            record.SourceService,
            record.SourceDocumentId,
            record.SkuCode,
            record.InspectedQuantity,
            record.BatchNo,
            record.SerialNo,
            record.UomCode,
            record.Result,
            record.DispositionReason,
            record.NonconformanceReportId,
            (record.ResultLines ?? []).Select(line => new BusinessConsoleInspectionRecordResultLine(
                line.CharacteristicCode,
                line.ObservedValue,
                line.MeasuredValue,
                line.UnitCode,
                line.Result,
                line.DefectReason,
                line.DefectQuantity)).ToArray(),
            record.CreatedAtUtc,
            record.AttemptNumber,
            record.ReinspectionOfInspectionRecordId);
    }

    public Task<BusinessConsoleQualitySpcControlChartResponse> QuerySpcControlChartAsync(
        string internalBearerToken,
        BusinessConsoleQualitySpcRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualitySpcControlChartResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/spc/control-chart?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("characteristicCode", request.CharacteristicCode),
                ("workCenterId", request.WorkCenterId),
                ("subgroupSize", request.SubgroupSize),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityProcessCapabilityResponse> QueryProcessCapabilityAsync(
        string internalBearerToken,
        BusinessConsoleQualityProcessCapabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityProcessCapabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/spc/process-capability?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("characteristicCode", request.CharacteristicCode),
                ("workCenterId", request.WorkCenterId),
                ("subgroupSize", request.SubgroupSize),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualitySpcControlChartListResponse> ListSpcControlChartsAsync(
        string internalBearerToken,
        BusinessConsoleQualitySpcControlChartListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualitySpcControlChartListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/spc/control-charts?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("characteristicCode", request.CharacteristicCode),
                ("workCenterId", request.WorkCenterId),
                ("locked", request.Locked),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityMeasuringDeviceListResponse> ListMeasuringDevicesAsync(
        string internalBearerToken,
        BusinessConsoleQualityMeasuringDeviceListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityMeasuringDeviceListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/measuring-devices?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceType", request.DeviceType),
                ("status", request.Status),
                ("calibrationState", request.CalibrationState),
                ("keyword", request.Keyword),
                ("warningDays", request.WarningDays),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityCalibrationRecordListResponse> ListCalibrationRecordsAsync(
        string internalBearerToken,
        BusinessConsoleQualityCalibrationRecordListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityCalibrationRecordListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/calibration-records?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("measuringDeviceId", request.MeasuringDeviceId),
                ("keyword", request.Keyword),
                ("calibratedFromUtc", request.CalibratedFromUtc),
                ("calibratedToUtc", request.CalibratedToUtc),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityCapaListResponse> ListCorrectiveActionsAsync(
        string internalBearerToken,
        BusinessConsoleQualityCapaListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityCapaListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/capas?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("ownerUserId", request.OwnerUserId),
                ("sourceNcrId", request.SourceNcrId),
                ("overdueOnly", request.OverdueOnly),
                ("keyword", request.Keyword),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityCapaItem> GetCorrectiveActionAsync(
        string internalBearerToken,
        string correctiveActionId,
        BusinessConsoleQualityCapaDetailRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityCapaItem>(
            internalBearerToken,
            HttpMethod.Get,
            CorrectiveActionPath(correctiveActionId) + "?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonListResponse> ListQualityReasonsAsync(
        string internalBearerToken,
        BusinessConsoleQualityReasonListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/quality/reason-codes?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("enabled", request.Enabled),
                ("search", request.Search),
                ("groupName", request.GroupName),
                ("skip", request.Skip),
                ("take", request.Take),
                ("defaultDisposition", request.DefaultDisposition)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);

    public Task<BusinessConsoleQualityReasonItem> GetQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Get,
            QualityReasonPath(reasonCode) + "?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> CreateQualityReasonAsync(
        string internalBearerToken,
        BusinessConsoleCreateQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/quality/reason-codes",
            request,
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> UpdateQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleUpdateQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Put,
            QualityReasonPath(reasonCode),
            request with { ReasonCode = reasonCode },
            cancellationToken);

    public Task<BusinessConsoleQualityReasonItem> ArchiveQualityReasonAsync(
        string internalBearerToken,
        string reasonCode,
        BusinessConsoleArchiveQualityReasonRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleQualityReasonItem>(
            internalBearerToken,
            HttpMethod.Post,
            QualityReasonPath(reasonCode) + "/archive",
            request with { ReasonCode = reasonCode },
            cancellationToken);

    public async Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.DispositionType, "rework", StringComparison.OrdinalIgnoreCase))
        {
            return await SendNcrDispositionAsync(internalBearerToken, ncrId, request, cancellationToken);
        }

        var current = await GetNcrAsync(
            internalBearerToken,
            new BusinessConsoleQualityNcrDetailRequest(ncrId, request.OrganizationId, request.EnvironmentId),
            cancellationToken);

        if (string.Equals(current.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var response = await SendNcrDispositionAsync(
                    internalBearerToken,
                    ncrId,
                    request,
                    cancellationToken);
                if (!response.Accepted)
                {
                    throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                        HttpStatusCode.BadGateway,
                        "downstream-invalid-response");
                }

                return AcceptedReworkResponse(ncrId, request);
            }
            catch (BusinessServiceProxyException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                current = await GetNcrAsync(
                    internalBearerToken,
                    new BusinessConsoleQualityNcrDetailRequest(ncrId, request.OrganizationId, request.EnvironmentId),
                    cancellationToken);
            }
        }

        if (!MatchesReworkDisposition(current, request))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.Conflict,
                "ncr-disposition-conflict");
        }

        return AcceptedReworkResponse(ncrId, request);
    }

    private static BusinessConsoleAcceptedResponse AcceptedReworkResponse(
        string ncrId,
        BusinessConsoleNcrDispositionRequest request)
    {
        var readbackPath = $"/api/business-console/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}?" + Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId));
        return new BusinessConsoleAcceptedResponse(
            Accepted: true,
            DownstreamService: "quality",
            DownstreamDocumentType: "ncr",
            DownstreamDocumentId: ncrId,
            OperationReceipt: BusinessConsoleOperationReceipts.Accepted(
                operationType: "quality.ncr.rework",
                authority: "quality",
                resourceType: "ncr",
                resourceId: ncrId,
                readbackPath,
                request.IdempotencyKey!));
    }

    private Task<BusinessConsoleAcceptedResponse> SendNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/disposition",
            new DownstreamSubmitNcrDispositionRequest(
                ncrId,
                request.DispositionType,
                request.DispositionApprovalChainId,
                request.AttachmentFileIds,
                request.MrbReviews?.Select(ToDownstreamMrbReview).ToArray()),
            cancellationToken);

    private static bool MatchesReworkDisposition(
        BusinessConsoleQualityNcrDetailResponse current,
        BusinessConsoleNcrDispositionRequest request) =>
        string.Equals(current.DispositionType, "rework", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            current.DispositionApprovalChainId?.Trim(),
            request.DispositionApprovalChainId?.Trim(),
            StringComparison.Ordinal);

    public Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        string actor,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleAcceptedResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/quality/ncrs/{Uri.EscapeDataString(ncrId)}/close",
            new DownstreamCloseNcrRequest(
                ncrId,
                request.ScrapMovementId,
                request.ReturnDocumentId,
                request.Reason),
            cancellationToken,
            configureRequest: message => message.Headers.TryAddWithoutValidation("X-Actor", actor));

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamInspectionPlanItem item) =>
        new(
            item.InspectionPlanId,
            item.PlanCode,
            item.Status,
            item.Category,
            item.SkuCode,
            item.PartnerId,
            item.WorkCenterId,
            item.DeviceAssetId,
            item.DocumentType,
            null,
            null,
            null,
            null,
            null,
            null,
            TimeIntervalHours: item.TimeIntervalHours,
            QuantityInterval: item.QuantityInterval,
            AssignedInspectorUserId: item.AssignedInspectorUserId,
            AssignedTeamId: item.AssignedTeamId);

    private static BusinessConsoleQualityNcrItem ToNcrItem(DownstreamNcrItem item) =>
        new(
            item.NcrId,
            item.NcrCode,
            item.Status,
            item.SourceType,
            item.SourceDocumentId,
            item.SkuCode,
            item.DefectQuantity,
            item.DefectReason,
            item.BatchNo,
            item.SerialNo,
            item.CloseReason,
            item.ReworkWorkOrderCreationStatus,
            item.ReworkWorkOrderId);

    private static BusinessConsoleQualityItem ToQualityItem(DownstreamInspectionRecordItem item) =>
        new(
            item.InspectionRecordId,
            item.InspectionRecordId,
            item.Result,
            null,
            item.SkuCode,
            null,
            null,
            null,
            null,
            item.SourceType,
            item.SourceDocumentId,
            null,
            item.DispositionReason,
            item.BatchNo,
            item.SerialNo,
            item.AttemptNumber,
            item.ReinspectionOfInspectionRecordId);

    private static string QualityReasonPath(string reasonCode) =>
        $"/api/business/v1/quality/reason-codes/{Uri.EscapeDataString(reasonCode)}";

    private static string CorrectiveActionPath(string correctiveActionId) =>
        $"/api/business/v1/quality/capas/{Uri.EscapeDataString(correctiveActionId)}";

    private sealed record DownstreamInspectionPlanListResponse(
        IReadOnlyCollection<DownstreamInspectionPlanItem> Items,
        int Total);

    private sealed record DownstreamCreateInspectionPlanResponse(string? InspectionPlanId);

    private sealed record DownstreamActivateInspectionPlanRequest(string InspectionPlanId);

    private sealed record DownstreamInspectionRecordListResponse(
        IReadOnlyCollection<DownstreamInspectionRecordItem> Items,
        int Total);

    private sealed record DownstreamInspectionTaskListResponse(
        IReadOnlyCollection<DownstreamInspectionTaskItem> Items,
        int Total);

    private sealed record DownstreamInspectionTaskDetailResponse(
        DownstreamInspectionTaskItem Task,
        string PlanCode,
        string Category,
        IReadOnlyCollection<DownstreamInspectionTaskCharacteristic> Characteristics);

    private sealed record DownstreamInspectionTaskCharacteristic(
        string CharacteristicCode,
        string Name,
        string Method,
        string Severity,
        bool IsRequired,
        string SamplingRule,
        string CharacteristicType,
        decimal? NominalValue,
        decimal? LowerSpecLimit,
        decimal? UpperSpecLimit,
        string? UnitCode);

    private sealed record DownstreamCreateInspectionRecordFromTaskResponse(
        string InspectionRecordId,
        string Result,
        string? NonconformanceReportId,
        string? NonconformanceReportCode,
        DateTimeOffset ChangedAtUtc);

    private sealed record DownstreamInspectionTaskItem(
        string InspectionTaskId,
        string InspectionPlanId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string? SourceDocumentLineId,
        string SkuCode,
        decimal Quantity,
        string UomCode,
        string? BatchNo,
        string? SerialNo,
        string Status,
        DateTimeOffset DueAtUtc,
        DateTimeOffset CreatedAtUtc,
        string? InspectionRecordId,
        string? AssignedInspectorUserId,
        string? AssignedTeamId,
        long Version,
        bool IsOverdue,
        IReadOnlyCollection<string>? AllowedActions,
        IReadOnlyCollection<string>? BlockReasons);

    private sealed record DownstreamCreateInspectionRecordFromTaskRequest(
        string InspectionTaskId,
        string OrganizationId,
        string EnvironmentId,
        string InspectorUserId,
        IReadOnlyCollection<DownstreamInspectionResultLine>? ResultLines,
        string? DispositionReason,
        IReadOnlyCollection<string>? DispositionAttachmentFileIds,
        string? IdempotencyKey = null);

    private static DownstreamCreateInspectionRecordRequest ToDownstreamRequest(
        BusinessConsoleCreateInspectionRecordRequest request) =>
        new(
            request.OrganizationId,
            request.EnvironmentId,
            request.InspectionPlanId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            request.InspectedQuantity,
            request.BatchNo,
            request.SerialNo,
            request.ResultLines?.Select(ToDownstreamLine).ToArray(),
            request.DispositionReason,
            request.DispositionAttachmentFileIds,
            request.StockRelease);

    private static DownstreamInspectionResultLine ToDownstreamLine(
        BusinessConsoleInspectionCharacteristicResult line) =>
        new(
            line.CharacteristicCode,
            line.ObservedValue,
            line.UnitCode,
            line.Result,
            line.DefectReason,
            line.DefectQuantity,
            line.AttachmentFileIds ?? [],
            line.MeasuredValue);

    private static string? JoinValues(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? null : string.Join(",", values);

    private static BusinessConsoleQualityInspectionTaskItem ToInspectionTaskItem(
        DownstreamInspectionTaskItem item) =>
        new(
            item.InspectionTaskId,
            item.InspectionPlanId,
            item.SourceType,
            item.SourceService,
            item.SourceDocumentId,
            item.SourceDocumentLineId,
            item.SkuCode,
            item.Quantity,
            item.UomCode,
            item.BatchNo,
            item.SerialNo,
            item.Status,
            item.DueAtUtc,
            item.CreatedAtUtc,
            item.InspectionRecordId,
            item.AssignedInspectorUserId,
            item.AssignedTeamId,
            item.Version,
            item.IsOverdue,
            item.AllowedActions ?? [],
            item.BlockReasons ?? []);

    private static DownstreamMrbReview ToDownstreamMrbReview(BusinessConsoleMrbReview review) =>
        new(
            review.ReviewerId,
            review.Decision,
            review.Comment,
            review.ReviewedAtUtc);

    private sealed record DownstreamCreateInspectionRecordRequest(
        string OrganizationId,
        string EnvironmentId,
        string? InspectionPlanId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string SkuCode,
        decimal InspectedQuantity,
        string? BatchNo,
        string? SerialNo,
        IReadOnlyCollection<DownstreamInspectionResultLine>? ResultLines,
        string? DispositionReason,
        IReadOnlyCollection<string>? DispositionAttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BusinessConsoleInspectionStockRelease? StockRelease);

    private sealed record DownstreamCreateReinspectionRequest(
        string OrganizationId,
        string EnvironmentId,
        IReadOnlyCollection<DownstreamInspectionResultLine>? ResultLines,
        string? DispositionReason,
        IReadOnlyCollection<string>? DispositionAttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MeasuringDeviceId);

    private sealed record DownstreamInspectionResultLine(
        string CharacteristicCode,
        string ObservedValue,
        string? UnitCode,
        string Result,
        string? DefectReason,
        decimal? DefectQuantity,
        IReadOnlyCollection<string> AttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MeasuredValue);

    private sealed record DownstreamInspectionPlanItem(
        string InspectionPlanId,
        string PlanCode,
        string Category,
        string? SkuCode,
        string? PartnerId,
        string? WorkCenterId,
        string? DeviceAssetId,
        string? DocumentType,
        int Version,
        string Status,
        IReadOnlyCollection<DownstreamInspectionPlanCharacteristic>? Characteristics,
        decimal? TimeIntervalHours = null,
        decimal? QuantityInterval = null,
        string? AssignedInspectorUserId = null,
        string? AssignedTeamId = null);

    private sealed record DownstreamInspectionPlanCharacteristic(
        string CharacteristicCode,
        string Name,
        string CharacteristicType,
        bool Required,
        decimal? NominalValue,
        decimal? LowerSpecLimit,
        decimal? UpperSpecLimit,
        string? UnitCode);

    private sealed record DownstreamInspectionRecordItem(
        string InspectionRecordId,
        string SourceType,
        string SourceDocumentId,
        string SkuCode,
        string Result,
        string? BatchNo,
        string? SerialNo,
        string? DispositionReason,
        int AttemptNumber = 1,
        string? ReinspectionOfInspectionRecordId = null);

    private sealed record DownstreamInspectionRecordDetail(
        string InspectionRecordId,
        string SourceType,
        string SourceService,
        string SourceDocumentId,
        string SkuCode,
        decimal InspectedQuantity,
        string? BatchNo,
        string? SerialNo,
        string? UomCode,
        string Result,
        string? DispositionReason,
        string? NonconformanceReportId,
        IReadOnlyCollection<DownstreamInspectionRecordDetailLine>? ResultLines,
        DateTime CreatedAtUtc,
        int AttemptNumber = 1,
        string? ReinspectionOfInspectionRecordId = null);

    private sealed record DownstreamInspectionRecordDetailLine(
        string CharacteristicCode,
        string ObservedValue,
        decimal? MeasuredValue,
        string? UnitCode,
        string Result,
        string? DefectReason,
        decimal? DefectQuantity);

    private sealed record DownstreamNcrListResponse(
        IReadOnlyCollection<DownstreamNcrItem> Items,
        int Total);

    private sealed record DownstreamNcrItem(
        string NcrId,
        string NcrCode,
        string SourceType,
        string SourceDocumentId,
        string SkuCode,
        decimal DefectQuantity,
        string DefectReason,
        string? BatchNo,
        string? SerialNo,
        string Status,
        string ReworkWorkOrderCreationStatus,
        string? SourceInspectionRecordId = null,
        string? DispositionType = null,
        string? DispositionApprovalChainId = null,
        string? CloseReason = null,
        string? ReworkWorkOrderId = null);

    private sealed record DownstreamSubmitNcrDispositionRequest(
        string NcrId,
        string DispositionType,
        string? DispositionApprovalChainId,
        IReadOnlyCollection<string>? AttachmentFileIds,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyCollection<DownstreamMrbReview>? MrbReviews);

    private sealed record DownstreamMrbReview(
        string ReviewerId,
        string Decision,
        string? Comment,
        DateTimeOffset ReviewedAtUtc);

    private sealed record DownstreamCloseNcrRequest(
        string NcrId,
        string? ScrapMovementId,
        string? ReturnDocumentId,
        string Reason);

    private sealed record DownstreamOpenNcrFromInspectionRequest(
        string InspectionRecordId,
        string OrganizationId,
        string EnvironmentId,
        string DefectReason,
        IReadOnlyCollection<string>? AttachmentFileIds);

    private sealed record DownstreamOpenNcrFromInspectionResponse(JsonElement NcrId);

    private static string FormatJsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.ToString(),
    };
}
