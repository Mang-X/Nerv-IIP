using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.InspectionRecords;

public sealed record CreateInspectionRecordRequest(
    string OrganizationId,
    string EnvironmentId,
    InspectionPlanId? InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string? BatchNo,
    string? SerialNo,
    IReadOnlyCollection<InspectionResultLineCommandInput>? ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string>? DispositionAttachmentFileIds);

public sealed record CreateInspectionRecordResponse(InspectionRecordId InspectionRecordId);

public sealed record OpenNcrFromInspectionRequest(
    InspectionRecordId InspectionRecordId,
    string DefectReason,
    IReadOnlyCollection<string>? AttachmentFileIds);

public sealed record OpenNcrFromInspectionResponse(NonconformanceReportId NcrId);

public sealed record ListInspectionRecordsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SourceService,
    string? SourceDocumentId,
    string? SourceType,
    string? SkuCode,
    string? Result,
    int Skip = 0,
    int Take = 100);

public sealed record ListInspectionRecordsEndpointResponse(IReadOnlyCollection<InspectionRecordResponse> Items, int Total);

public sealed class CreateInspectionRecordEndpoint(ISender sender)
    : QualityEndpoint<CreateInspectionRecordRequest, ResponseData<CreateInspectionRecordResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<CreateInspectionRecordEndpoint>());
    }

    public override async Task HandleAsync(CreateInspectionRecordRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateInspectionRecordCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.InspectionPlanId,
            req.SourceType,
            req.SourceService,
            req.SourceDocumentId,
            req.SkuCode,
            req.InspectedQuantity,
            req.BatchNo,
            req.SerialNo,
            req.ResultLines ?? [],
            req.DispositionReason,
            req.DispositionAttachmentFileIds ?? []), ct);
        await Send.OkAsync(new CreateInspectionRecordResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class OpenNcrFromInspectionEndpoint(ISender sender)
    : QualityEndpoint<OpenNcrFromInspectionRequest, ResponseData<OpenNcrFromInspectionResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<OpenNcrFromInspectionEndpoint>());
    }

    public override async Task HandleAsync(OpenNcrFromInspectionRequest req, CancellationToken ct)
    {
        var ncrId = await sender.Send(new OpenNcrFromInspectionCommand(
            req.InspectionRecordId,
            req.DefectReason,
            req.AttachmentFileIds ?? []), ct);
        await Send.OkAsync(new OpenNcrFromInspectionResponse(ncrId).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListInspectionRecordsEndpoint(ISender sender)
    : QualityEndpoint<ListInspectionRecordsRequest, ResponseData<ListInspectionRecordsEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<ListInspectionRecordsEndpoint>());
    }

    public override async Task HandleAsync(ListInspectionRecordsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListInspectionRecordsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.SourceService,
            req.SourceDocumentId,
            req.SourceType,
            req.SkuCode,
            req.Result,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(new ListInspectionRecordsEndpointResponse(response.Items, response.Total).AsResponseData(), cancellation: ct);
    }
}
