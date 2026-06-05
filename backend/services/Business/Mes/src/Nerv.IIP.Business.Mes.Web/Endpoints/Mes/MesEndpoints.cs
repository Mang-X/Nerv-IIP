using FastEndpoints;
using FluentValidation;
using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.ServiceAuth;
using System.Diagnostics.CodeAnalysis;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

public sealed record RunScheduleRequest(
    string OrganizationId,
    string EnvironmentId,
    RescheduleTrigger Trigger);

public sealed record CreateRushWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string? OperationTaskId,
    int? OperationSequence,
    int DurationMinutes,
    string? IdempotencyKey = null);

public sealed record ListMesWorkOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100);

public sealed record RecordProductionReportRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    bool CompletesOperation,
    DateTimeOffset ReportedAtUtc,
    string? IdempotencyKey = null,
    IReadOnlyCollection<ConsumedMaterialLotInput>? ConsumedMaterialLots = null);

public sealed record RecordProductionReportResponse(
    global::Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate.ProductionReportId ProductionReportId,
    string ReportNo);

public sealed record ListProductionReportsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100);

public sealed record CreateFinishedGoodsReceiptRequestRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc,
    string? IdempotencyKey = null);

public sealed record CreateFinishedGoodsReceiptRequestResponse(
    global::Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequestId FinishedGoodsReceiptRequestId,
    string RequestNo);

public sealed record ListFinishedGoodsReceiptRequestsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100);

public sealed record ListCapacityImpactsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    int Skip = 0,
    int Take = 100);

public sealed record FoundationReadinessAreaRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string AreaCode,
    string? SiteCode,
    string? LineCode,
    string? WorkCenterCode,
    string? SkuId,
    string? ProductionVersionId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record MesContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record WorkOrderContextRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string WorkOrderId);

public sealed record ProductionPlanContextRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string ProductionPlanId);

public sealed record ConvertPlanToWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string ProductionPlanId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal PlannedQuantity,
    string UomCode,
    DateTimeOffset? DueUtc,
    string? WorkCenterId,
    DateTimeOffset? RequestedAtUtc,
    string? SourceSystem = null,
    string? SourceDocumentType = null,
    string? SourceDocumentId = null,
    string? SourceDemandReference = null,
    string? IdempotencyKey = null);

public sealed class ConvertPlanToWorkOrderRequestValidator : Validator<ConvertPlanToWorkOrderRequest>
{
    public ConvertPlanToWorkOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductionPlanId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductionVersionId).MaximumLength(100);
        RuleFor(x => x.PlannedQuantity).GreaterThan(0);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DueUtc).NotNull();
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.SourceSystem).MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(100);
        RuleFor(x => x.SourceDemandReference).MaximumLength(100);
    }
}

public sealed record ReleaseWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string WorkOrderId,
    DateTimeOffset? ReleasedAtUtc);

public sealed record CreateMaterialIssueRequestRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    decimal? Quantity,
    DateTimeOffset? RequestedAtUtc,
    string? IdempotencyKey = null);

public sealed record ListMaterialIssueRequestsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100);

public sealed record LineSideMaterialReceiptRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string RequestId,
    DateTimeOffset? ReceivedAtUtc,
    decimal? ReceivedQuantity = null,
    string? MaterialLotId = null);

public sealed record AssignDispatchTaskRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string OperationTaskId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    DateTimeOffset? AssignedAtUtc);

public sealed record OperationTaskActionRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string OperationTaskId,
    DateTimeOffset? ChangedAtUtc);

public sealed record RecordDefectRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset? RecordedAtUtc,
    string? IdempotencyKey = null);

public sealed record ListRelatedQualityItemsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string? OperationTaskId,
    int Skip = 0,
    int Take = 100);

public sealed record ListDowntimeEventsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkCenterId,
    string? DeviceAssetId,
    int Skip = 0,
    int Take = 100);

public sealed record RecordDowntimeEventRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string? OperationTaskId,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? ReasonCode,
    string? Reason,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? IdempotencyKey = null);

public sealed record RecoverDowntimeRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string DowntimeEventId,
    DateTimeOffset? RecoveredAtUtc);

public sealed record ListShiftHandoversRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ShiftId,
    int Skip = 0,
    int Take = 100);

public sealed record CreateShiftHandoverRequest(
    string OrganizationId,
    string EnvironmentId,
    string ShiftId,
    string TeamId,
    DateTimeOffset? HandoverAtUtc,
    string? IdempotencyKey = null);

public sealed record AcceptShiftHandoverRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string HandoverId,
    DateTimeOffset? AcceptedAtUtc);

public sealed record TraceabilityWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string WorkOrderId);

public sealed record TraceabilityBatchRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string BatchOrSerial);

public sealed record TraceabilityMaterialLotRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string MaterialLotId);

public abstract class MesEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureMesContract(MesEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by MES endpoints.");
        }

        Tags("Business MES");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }
}

public sealed class RunScheduleEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<RunScheduleRequest, MesScheduleResult>
{
    public override void Configure()
    {
        ConfigureMesContract(MesEndpointContracts.Get<RunScheduleEndpoint>());
    }

    public override async Task HandleAsync(RunScheduleRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RescheduleCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.Trigger,
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(result, ct);
    }
}

public sealed class GetFoundationReadinessAreaEndpoint(ISender sender)
    : MesEndpoint<FoundationReadinessAreaRequest, MesReadinessArea>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetFoundationReadinessAreaEndpoint>());

    public override async Task HandleAsync(FoundationReadinessAreaRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMesFoundationReadinessAreaQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.AreaCode,
            req.SiteCode,
            req.LineCode,
            req.WorkCenterCode,
            req.SkuId,
            req.ProductionVersionId,
            req.PlannedStartUtc,
            req.PlannedEndUtc), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetMesOverviewEndpoint(ISender sender)
    : MesEndpoint<MesContextRequest, MesOverviewResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetMesOverviewEndpoint>());

    public override async Task HandleAsync(MesContextRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMesOverviewQuery(req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListProductionPlansEndpoint(ISender sender)
    : MesEndpoint<ListMesWorkOrdersRequest, MesProductionPlanListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListProductionPlansEndpoint>());

    public override async Task HandleAsync(ListMesWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListProductionPlansQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetProductionPlanReadinessEndpoint(ISender sender)
    : MesEndpoint<ProductionPlanContextRequest, MesProductionPlanReadinessResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetProductionPlanReadinessEndpoint>());

    public override async Task HandleAsync(ProductionPlanContextRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetProductionPlanReadinessQuery(req.OrganizationId, req.EnvironmentId, req.ProductionPlanId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ConvertPlanToWorkOrderEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<ConvertPlanToWorkOrderRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ConvertPlanToWorkOrderEndpoint>());

    public override async Task HandleAsync(ConvertPlanToWorkOrderRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ConvertPlanToWorkOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.ProductionPlanId,
            req.WorkOrderId,
            req.RequestedAtUtc ?? timeProvider.GetUtcNow(),
            req.SkuId,
            req.ProductionVersionId,
            req.PlannedQuantity,
            req.UomCode,
            req.DueUtc.GetValueOrDefault(),
            req.WorkCenterId,
            req.SourceSystem,
            req.SourceDocumentType,
            req.SourceDocumentId,
            req.SourceDemandReference,
            req.IdempotencyKey), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class CreateRushWorkOrderEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<CreateRushWorkOrderRequest, CreateRushWorkOrderResponse>
{
    public override void Configure()
    {
        ConfigureMesContract(MesEndpointContracts.Get<CreateRushWorkOrderEndpoint>());
    }

    public override async Task HandleAsync(CreateRushWorkOrderRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateRushWorkOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.SkuId,
            req.ProductionVersionId,
            req.Quantity,
            req.DueUtc,
            req.WorkCenterId,
            req.OperationTaskId,
            req.OperationSequence ?? 10,
            TimeSpan.FromMinutes(req.DurationMinutes),
            timeProvider.GetUtcNow(),
            req.IdempotencyKey), ct);
        await Send.OkAsync(result, ct);
    }
}

public sealed class ListMesWorkOrdersEndpoint(ISender sender)
    : MesEndpoint<ListMesWorkOrdersRequest, ListMesWorkOrdersResponse>
{
    public override void Configure()
    {
        ConfigureMesContract(MesEndpointContracts.Get<ListMesWorkOrdersEndpoint>());
    }

    public override async Task HandleAsync(ListMesWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new ListMesWorkOrdersQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Skip, req.Take),
            ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetMesWorkOrderDetailEndpoint(ISender sender)
    : MesEndpoint<WorkOrderContextRequest, MesWorkOrderDetailResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetMesWorkOrderDetailEndpoint>());

    public override async Task HandleAsync(WorkOrderContextRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMesWorkOrderDetailQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ReleaseWorkOrderEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<ReleaseWorkOrderRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ReleaseWorkOrderEndpoint>());

    public override async Task HandleAsync(ReleaseWorkOrderRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ReleaseWorkOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.ReleasedAtUtc ?? timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetMaterialReadinessEndpoint(ISender sender)
    : MesEndpoint<WorkOrderContextRequest, MesMaterialReadinessResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetMaterialReadinessEndpoint>());

    public override async Task HandleAsync(WorkOrderContextRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMaterialReadinessQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class CreateMaterialIssueRequestEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<CreateMaterialIssueRequestRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<CreateMaterialIssueRequestEndpoint>());

    public override async Task HandleAsync(CreateMaterialIssueRequestRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new CreateMaterialIssueRequestCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.OperationTaskId,
            req.MaterialId,
            req.Quantity,
            req.RequestedAtUtc ?? timeProvider.GetUtcNow(),
            req.IdempotencyKey), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListMaterialIssueRequestsEndpoint(ISender sender)
    : MesEndpoint<ListMaterialIssueRequestsRequest, MesMaterialIssueRequestListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListMaterialIssueRequestsEndpoint>());

    public override async Task HandleAsync(ListMaterialIssueRequestsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListMaterialIssueRequestsQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ConfirmLineSideMaterialReceiptEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<LineSideMaterialReceiptRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ConfirmLineSideMaterialReceiptEndpoint>());

    public override async Task HandleAsync(LineSideMaterialReceiptRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ConfirmLineSideMaterialReceiptCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.RequestId,
            req.ReceivedAtUtc ?? timeProvider.GetUtcNow(),
            req.ReceivedQuantity,
            req.MaterialLotId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListDispatchTasksEndpoint(ISender sender)
    : MesEndpoint<ListMesWorkOrdersRequest, MesDispatchTaskListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListDispatchTasksEndpoint>());

    public override async Task HandleAsync(ListMesWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListDispatchTasksQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class AssignDispatchTaskEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<AssignDispatchTaskRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<AssignDispatchTaskEndpoint>());

    public override async Task HandleAsync(AssignDispatchTaskRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new AssignDispatchTaskCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.OperationTaskId,
            req.AssignedUserId,
            req.DeviceAssetId,
            req.ShiftId,
            req.AssignedAtUtc ?? timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListOperationTasksEndpoint(ISender sender)
    : MesEndpoint<ListMesWorkOrdersRequest, MesOperationTaskListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListOperationTasksEndpoint>());

    public override async Task HandleAsync(ListMesWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListOperationTasksQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public abstract class OperationTaskActionEndpoint(string action, ISender sender, TimeProvider timeProvider)
    : MesEndpoint<OperationTaskActionRequest, MesOperationActionResponse>
{
    public override async Task HandleAsync(OperationTaskActionRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ChangeOperationTaskStateCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.OperationTaskId,
            action,
            req.ChangedAtUtc ?? timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class StartOperationTaskEndpoint(ISender sender, TimeProvider timeProvider)
    : OperationTaskActionEndpoint("start", sender, timeProvider)
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<StartOperationTaskEndpoint>());
}

public sealed class PauseOperationTaskEndpoint(ISender sender, TimeProvider timeProvider)
    : OperationTaskActionEndpoint("pause", sender, timeProvider)
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<PauseOperationTaskEndpoint>());
}

public sealed class ResumeOperationTaskEndpoint(ISender sender, TimeProvider timeProvider)
    : OperationTaskActionEndpoint("resume", sender, timeProvider)
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ResumeOperationTaskEndpoint>());
}

public sealed class CompleteOperationTaskEndpoint(ISender sender, TimeProvider timeProvider)
    : OperationTaskActionEndpoint("complete", sender, timeProvider)
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<CompleteOperationTaskEndpoint>());
}

public sealed class GetWipSummaryEndpoint(ISender sender)
    : MesEndpoint<ListMesWorkOrdersRequest, MesWipSummaryResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetWipSummaryEndpoint>());

    public override async Task HandleAsync(ListMesWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetWipSummaryQuery(req.OrganizationId, req.EnvironmentId, req.Status, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class RecordProductionReportEndpoint(ISender sender)
    : MesEndpoint<RecordProductionReportRequest, RecordProductionReportResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<RecordProductionReportEndpoint>());

    public override async Task HandleAsync(RecordProductionReportRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RecordProductionReportCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.OperationTaskId,
            req.GoodQuantity,
            req.ScrapQuantity,
            req.CompletesOperation,
            req.ReportedAtUtc,
            req.IdempotencyKey,
            req.ConsumedMaterialLots), ct);
        await Send.OkAsync(new RecordProductionReportResponse(result.Id, result.ReportNo), ct);
    }
}

public sealed class ListProductionReportsEndpoint(ISender sender)
    : MesEndpoint<ListProductionReportsRequest, ListProductionReportsResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListProductionReportsEndpoint>());

    public override async Task HandleAsync(ListProductionReportsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListProductionReportsQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class RecordDefectEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<RecordDefectRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<RecordDefectEndpoint>());

    public override async Task HandleAsync(RecordDefectRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new RecordDefectCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.OperationTaskId,
            req.DefectCode,
            req.Quantity,
            req.RecordedAtUtc ?? timeProvider.GetUtcNow(),
            req.IdempotencyKey), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListRelatedQualityItemsEndpoint(ISender sender)
    : MesEndpoint<ListRelatedQualityItemsRequest, MesRelatedQualityItemListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListRelatedQualityItemsEndpoint>());

    public override async Task HandleAsync(ListRelatedQualityItemsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListRelatedQualityItemsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.OperationTaskId,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class CreateFinishedGoodsReceiptRequestEndpoint(ISender sender)
    : MesEndpoint<CreateFinishedGoodsReceiptRequestRequest, CreateFinishedGoodsReceiptRequestResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<CreateFinishedGoodsReceiptRequestEndpoint>());

    public override async Task HandleAsync(CreateFinishedGoodsReceiptRequestRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateFinishedGoodsReceiptRequestCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.SkuId,
            req.Quantity,
            req.UomCode,
            req.RequestedAtUtc,
            req.IdempotencyKey), ct);
        await Send.OkAsync(new CreateFinishedGoodsReceiptRequestResponse(result.Id, result.RequestNo), ct);
    }
}

public sealed class ListFinishedGoodsReceiptRequestsEndpoint(ISender sender)
    : MesEndpoint<ListFinishedGoodsReceiptRequestsRequest, ListFinishedGoodsReceiptRequestsResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListFinishedGoodsReceiptRequestsEndpoint>());

    public override async Task HandleAsync(ListFinishedGoodsReceiptRequestsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListFinishedGoodsReceiptRequestsQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListDowntimeEventsEndpoint(ISender sender)
    : MesEndpoint<ListDowntimeEventsRequest, MesDowntimeEventListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListDowntimeEventsEndpoint>());

    public override async Task HandleAsync(ListDowntimeEventsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListDowntimeEventsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkCenterId,
            req.DeviceAssetId,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class RecordDowntimeEventEndpoint(ISender sender)
    : MesEndpoint<RecordDowntimeEventRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<RecordDowntimeEventEndpoint>());

    public override async Task HandleAsync(RecordDowntimeEventRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new RecordDowntimeEventCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.OperationTaskId,
            req.WorkCenterId ?? req.WorkOrderId ?? "unknown-work-center",
            req.DeviceAssetId,
            req.Reason ?? req.ReasonCode ?? "manual-downtime",
            req.FromUtc ?? req.StartedAtUtc ?? DateTimeOffset.UtcNow,
            req.ToUtc,
            req.IdempotencyKey), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ConfirmDowntimeRecoveryEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<RecoverDowntimeRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ConfirmDowntimeRecoveryEndpoint>());

    public override async Task HandleAsync(RecoverDowntimeRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ConfirmDowntimeRecoveryCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.DowntimeEventId,
            req.RecoveredAtUtc ?? timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListShiftHandoversEndpoint(ISender sender)
    : MesEndpoint<ListShiftHandoversRequest, MesShiftHandoverListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListShiftHandoversEndpoint>());

    public override async Task HandleAsync(ListShiftHandoversRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListShiftHandoversQuery(req.OrganizationId, req.EnvironmentId, req.ShiftId, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class CreateShiftHandoverEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<CreateShiftHandoverRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<CreateShiftHandoverEndpoint>());

    public override async Task HandleAsync(CreateShiftHandoverRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new CreateShiftHandoverCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.ShiftId,
            req.TeamId,
            req.HandoverAtUtc ?? timeProvider.GetUtcNow(),
            req.IdempotencyKey), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class AcceptShiftHandoverEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<AcceptShiftHandoverRequest, MesAcceptedResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<AcceptShiftHandoverEndpoint>());

    public override async Task HandleAsync(AcceptShiftHandoverRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new AcceptShiftHandoverCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.HandoverId,
            req.AcceptedAtUtc ?? timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetWorkOrderTraceabilityEndpoint(ISender sender)
    : MesEndpoint<TraceabilityWorkOrderRequest, MesTraceabilityResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetWorkOrderTraceabilityEndpoint>());

    public override async Task HandleAsync(TraceabilityWorkOrderRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetWorkOrderTraceabilityQuery(req.OrganizationId, req.EnvironmentId, req.WorkOrderId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetBatchTraceabilityEndpoint(ISender sender)
    : MesEndpoint<TraceabilityBatchRequest, MesTraceabilityResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetBatchTraceabilityEndpoint>());

    public override async Task HandleAsync(TraceabilityBatchRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetBatchTraceabilityQuery(req.OrganizationId, req.EnvironmentId, req.BatchOrSerial), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetMaterialLotTraceabilityEndpoint(ISender sender)
    : MesEndpoint<TraceabilityMaterialLotRequest, MesTraceabilityResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetMaterialLotTraceabilityEndpoint>());

    public override async Task HandleAsync(TraceabilityMaterialLotRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetMaterialLotTraceabilityQuery(req.OrganizationId, req.EnvironmentId, req.MaterialLotId), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class ListCapacityImpactsEndpoint(ISender sender)
    : MesEndpoint<ListCapacityImpactsRequest, ListCapacityImpactsResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListCapacityImpactsEndpoint>());

    public override async Task HandleAsync(ListCapacityImpactsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListCapacityImpactsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Skip, req.Take), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed record MesEndpointContract(
    Type EndpointType,
    string HttpMethod,
    string Route,
    string PermissionCode,
    string OperationId);

public static class MesEndpointContracts
{
    public static readonly IReadOnlyCollection<MesEndpointContract> All =
    [
        new(typeof(GetFoundationReadinessAreaEndpoint), "GET", "/api/business/v1/mes/foundation-readiness/{areaCode}", MesPermissionCodes.FoundationRead, "getBusinessMesFoundationReadinessArea"),
        new(typeof(GetMesOverviewEndpoint), "GET", "/api/business/v1/mes/overview", MesPermissionCodes.OverviewRead, "getBusinessMesOverview"),
        new(typeof(ListProductionPlansEndpoint), "GET", "/api/business/v1/mes/production-plans", MesPermissionCodes.PlansRead, "listBusinessMesProductionPlans"),
        new(typeof(GetProductionPlanReadinessEndpoint), "GET", "/api/business/v1/mes/production-plans/{productionPlanId}/readiness", MesPermissionCodes.PlansRead, "getBusinessMesProductionPlanReadiness"),
        new(typeof(ConvertPlanToWorkOrderEndpoint), "POST", "/api/business/v1/mes/production-plans/{productionPlanId}/work-orders", MesPermissionCodes.WorkOrdersManage, "convertBusinessMesPlanToWorkOrder"),
        new(typeof(RunScheduleEndpoint), "POST", "/api/business/v1/mes/schedules/run", MesPermissionCodes.SchedulesManage, "runBusinessMesSchedule"),
        new(typeof(CreateRushWorkOrderEndpoint), "POST", "/api/business/v1/mes/work-orders/rush", MesPermissionCodes.WorkOrdersManage, "createBusinessMesRushWorkOrder"),
        new(typeof(ListMesWorkOrdersEndpoint), "GET", "/api/business/v1/mes/work-orders", MesPermissionCodes.WorkOrdersRead, "listBusinessMesWorkOrders"),
        new(typeof(GetMesWorkOrderDetailEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}", MesPermissionCodes.WorkOrdersRead, "getBusinessMesWorkOrderDetail"),
        new(typeof(ReleaseWorkOrderEndpoint), "POST", "/api/business/v1/mes/work-orders/{workOrderId}/release", MesPermissionCodes.WorkOrdersManage, "releaseBusinessMesWorkOrder"),
        new(typeof(GetMaterialReadinessEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness", MesPermissionCodes.MaterialsRead, "getBusinessMesMaterialReadiness"),
        new(typeof(CreateMaterialIssueRequestEndpoint), "POST", "/api/business/v1/mes/work-orders/{workOrderId}/material-issue-requests", MesPermissionCodes.MaterialsManage, "createBusinessMesMaterialIssueRequest"),
        new(typeof(ListMaterialIssueRequestsEndpoint), "GET", "/api/business/v1/mes/material-issue-requests", MesPermissionCodes.MaterialsRead, "listBusinessMesMaterialIssueRequests"),
        new(typeof(ConfirmLineSideMaterialReceiptEndpoint), "POST", "/api/business/v1/mes/material-issue-requests/{requestId}/line-side-receipts", MesPermissionCodes.MaterialsManage, "confirmBusinessMesLineSideMaterialReceipt"),
        new(typeof(ListDispatchTasksEndpoint), "GET", "/api/business/v1/mes/dispatch-tasks", MesPermissionCodes.DispatchRead, "listBusinessMesDispatchTasks"),
        new(typeof(AssignDispatchTaskEndpoint), "POST", "/api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign", MesPermissionCodes.DispatchManage, "assignBusinessMesDispatchTask"),
        new(typeof(ListOperationTasksEndpoint), "GET", "/api/business/v1/mes/operation-tasks", MesPermissionCodes.OperationsRead, "listBusinessMesOperationTasks"),
        new(typeof(StartOperationTaskEndpoint), "POST", "/api/business/v1/mes/operation-tasks/{operationTaskId}/start", MesPermissionCodes.OperationsManage, "startBusinessMesOperationTask"),
        new(typeof(PauseOperationTaskEndpoint), "POST", "/api/business/v1/mes/operation-tasks/{operationTaskId}/pause", MesPermissionCodes.OperationsManage, "pauseBusinessMesOperationTask"),
        new(typeof(ResumeOperationTaskEndpoint), "POST", "/api/business/v1/mes/operation-tasks/{operationTaskId}/resume", MesPermissionCodes.OperationsManage, "resumeBusinessMesOperationTask"),
        new(typeof(CompleteOperationTaskEndpoint), "POST", "/api/business/v1/mes/operation-tasks/{operationTaskId}/complete", MesPermissionCodes.OperationsManage, "completeBusinessMesOperationTask"),
        new(typeof(GetWipSummaryEndpoint), "GET", "/api/business/v1/mes/wip", MesPermissionCodes.OperationsRead, "getBusinessMesWipSummary"),
        new(typeof(RecordProductionReportEndpoint), "POST", "/api/business/v1/mes/production-reports", MesPermissionCodes.ReportingWrite, "recordBusinessMesProductionReport"),
        new(typeof(ListProductionReportsEndpoint), "GET", "/api/business/v1/mes/production-reports", MesPermissionCodes.ReportingRead, "listBusinessMesProductionReports"),
        new(typeof(RecordDefectEndpoint), "POST", "/api/business/v1/mes/defects", MesPermissionCodes.QualityWrite, "recordBusinessMesDefect"),
        new(typeof(ListRelatedQualityItemsEndpoint), "GET", "/api/business/v1/mes/related-quality-items", MesPermissionCodes.QualityRead, "listBusinessMesRelatedQualityItems"),
        new(typeof(CreateFinishedGoodsReceiptRequestEndpoint), "POST", "/api/business/v1/mes/finished-goods-receipt-requests", MesPermissionCodes.ReceiptsManage, "createBusinessMesFinishedGoodsReceiptRequest"),
        new(typeof(ListFinishedGoodsReceiptRequestsEndpoint), "GET", "/api/business/v1/mes/finished-goods-receipt-requests", MesPermissionCodes.ReceiptsRead, "listBusinessMesFinishedGoodsReceiptRequests"),
        new(typeof(ListDowntimeEventsEndpoint), "GET", "/api/business/v1/mes/downtime-events", MesPermissionCodes.DowntimeRead, "listBusinessMesDowntimeEvents"),
        new(typeof(RecordDowntimeEventEndpoint), "POST", "/api/business/v1/mes/downtime-events", MesPermissionCodes.DowntimeManage, "recordBusinessMesDowntimeEvent"),
        new(typeof(ConfirmDowntimeRecoveryEndpoint), "POST", "/api/business/v1/mes/downtime-events/{downtimeEventId}/recover", MesPermissionCodes.DowntimeManage, "confirmBusinessMesDowntimeRecovery"),
        new(typeof(ListShiftHandoversEndpoint), "GET", "/api/business/v1/mes/shift-handovers", MesPermissionCodes.HandoversRead, "listBusinessMesShiftHandovers"),
        new(typeof(CreateShiftHandoverEndpoint), "POST", "/api/business/v1/mes/shift-handovers", MesPermissionCodes.HandoversManage, "createBusinessMesShiftHandover"),
        new(typeof(AcceptShiftHandoverEndpoint), "POST", "/api/business/v1/mes/shift-handovers/{handoverId}/accept", MesPermissionCodes.HandoversManage, "acceptBusinessMesShiftHandover"),
        new(typeof(GetWorkOrderTraceabilityEndpoint), "GET", "/api/business/v1/mes/traceability/work-orders/{workOrderId}", MesPermissionCodes.TraceabilityRead, "getBusinessMesWorkOrderTraceability"),
        new(typeof(GetBatchTraceabilityEndpoint), "GET", "/api/business/v1/mes/traceability/batches/{batchOrSerial}", MesPermissionCodes.TraceabilityRead, "getBusinessMesBatchTraceability"),
        new(typeof(GetMaterialLotTraceabilityEndpoint), "GET", "/api/business/v1/mes/traceability/material-lots/{materialLotId}", MesPermissionCodes.TraceabilityRead, "getBusinessMesMaterialLotTraceability"),
        new(typeof(ListCapacityImpactsEndpoint), "GET", "/api/business/v1/mes/capacity-impacts", MesPermissionCodes.CapacityRead, "listBusinessMesCapacityImpacts"),
    ];

    public static MesEndpointContract Get<TEndpoint>()
    {
        return All.Single(x => x.EndpointType == typeof(TEndpoint));
    }

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out MesEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
