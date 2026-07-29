using FluentValidation;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.InspectionTasks;

public sealed record ListInspectionTasksRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? SkuCode,
    int Skip = 0,
    int Take = 100,
    InspectionTaskId? InspectionTaskId = null);

public sealed record ListInspectionTasksEndpointResponse(IReadOnlyCollection<InspectionTaskResponse> Items, int Total);

public sealed record CreateInspectionRecordFromTaskRequest(
    InspectionTaskId InspectionTaskId,
    string InspectorUserId,
    IReadOnlyCollection<InspectionResultLineCommandInput>? ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string>? DispositionAttachmentFileIds,
    string IdempotencyKey,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public sealed class CreateInspectionRecordFromTaskRequestValidator
    : FastEndpoints.Validator<CreateInspectionRecordFromTaskRequest>
{
    public CreateInspectionRecordFromTaskRequestValidator()
    {
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

/// <summary>权威检验结论：记录 id、后端计算的 result，以及不合格时自动开出并回链的 NCR id 与业务编号。</summary>
public sealed record CreateInspectionRecordFromTaskEndpointResponse(
    InspectionRecordId InspectionRecordId,
    string Result,
    string? NonconformanceReportId,
    string? NonconformanceReportCode,
    DateTimeOffset ChangedAtUtc);

public sealed class ListInspectionTasksEndpoint(ISender sender)
    : QualityEndpoint<ListInspectionTasksRequest, ResponseData<ListInspectionTasksEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<ListInspectionTasksEndpoint>());
    }

    public override async Task HandleAsync(ListInspectionTasksRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListInspectionTasksQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Status,
            req.SkuCode,
            req.Skip,
            req.Take,
            req.InspectionTaskId), ct);
        await Send.OkAsync(new ListInspectionTasksEndpointResponse(response.Items, response.Total).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateInspectionRecordFromTaskEndpoint(ISender sender)
    : QualityEndpoint<CreateInspectionRecordFromTaskRequest, ResponseData<CreateInspectionRecordFromTaskEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(
            QualityInspectionEndpointContracts.Get<CreateInspectionRecordFromTaskEndpoint>(),
            StatusCodes.Status409Conflict);
    }

    public override async Task HandleAsync(CreateInspectionRecordFromTaskRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateInspectionRecordFromTaskCommand(
            req.InspectionTaskId,
            req.InspectorUserId,
            req.ResultLines ?? [],
            req.DispositionReason,
            req.DispositionAttachmentFileIds ?? [],
            req.IdempotencyKey,
            req.OrganizationId,
            req.EnvironmentId), ct);
        await Send.OkAsync(
            new CreateInspectionRecordFromTaskEndpointResponse(
                result.InspectionRecordId,
                result.Result,
                result.NonconformanceReportId,
                result.NonconformanceReportCode,
                result.ChangedAtUtc).AsResponseData(),
            cancellation: ct);
    }
}
