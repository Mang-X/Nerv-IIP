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
    InspectionTaskId? InspectionTaskId = null,
    string? ScopeKind = null,
    string? PrincipalId = null,
    IReadOnlyCollection<string>? AuthorizedTeamIds = null,
    string? SourceType = null,
    string? SourceService = null,
    string? Keyword = null,
    bool? Overdue = null);

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

public sealed record GetInspectionTaskRequest(
    InspectionTaskId InspectionTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ScopeKind,
    string PrincipalId,
    IReadOnlyCollection<string>? AuthorizedTeamIds = null);

public sealed record AssignInspectionTaskRequest(
    InspectionTaskId InspectionTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    string? AssignedInspectorUserId,
    string? AssignedTeamId,
    string? Reason,
    string IdempotencyKey,
    long ExpectedVersion);

public sealed record ClaimInspectionTaskRequest(
    InspectionTaskId InspectionTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string>? AuthorizedTeamIds,
    string IdempotencyKey,
    long ExpectedVersion);

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
            req.InspectionTaskId,
            req.ScopeKind,
            req.PrincipalId,
            req.AuthorizedTeamIds,
            req.SourceType,
            req.SourceService,
            req.Keyword,
            req.Overdue), ct);
        await Send.OkAsync(new ListInspectionTasksEndpointResponse(response.Items, response.Total).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetInspectionTaskEndpoint(ISender sender)
    : QualityEndpoint<GetInspectionTaskRequest, ResponseData<InspectionTaskDetailResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityInspectionEndpointContracts.Get<GetInspectionTaskEndpoint>());
    }

    public override async Task HandleAsync(GetInspectionTaskRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new GetInspectionTaskQuery(
            req.InspectionTaskId,
            req.OrganizationId,
            req.EnvironmentId,
            req.ScopeKind,
            req.PrincipalId,
            req.AuthorizedTeamIds ?? []), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class AssignInspectionTaskEndpoint(ISender sender)
    : QualityEndpoint<AssignInspectionTaskRequest, ResponseData<InspectionTaskAssignmentResult>>
{
    public override void Configure()
    {
        ConfigureQualityContract(
            QualityInspectionEndpointContracts.Get<AssignInspectionTaskEndpoint>(),
            StatusCodes.Status403Forbidden,
            StatusCodes.Status409Conflict,
            StatusCodes.Status422UnprocessableEntity);
    }

    public override async Task HandleAsync(AssignInspectionTaskRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new AssignInspectionTaskCommand(
            req.InspectionTaskId,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AssignedInspectorUserId,
            req.AssignedTeamId,
            req.Reason,
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class ClaimInspectionTaskEndpoint(ISender sender)
    : QualityEndpoint<ClaimInspectionTaskRequest, ResponseData<InspectionTaskAssignmentResult>>
{
    public override void Configure()
    {
        ConfigureQualityContract(
            QualityInspectionEndpointContracts.Get<ClaimInspectionTaskEndpoint>(),
            StatusCodes.Status403Forbidden,
            StatusCodes.Status409Conflict,
            StatusCodes.Status422UnprocessableEntity);
    }

    public override async Task HandleAsync(ClaimInspectionTaskRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ClaimInspectionTaskCommand(
            req.InspectionTaskId,
            req.OrganizationId,
            req.EnvironmentId,
            req.ActorPrincipalId,
            req.AuthorizedTeamIds ?? [],
            req.IdempotencyKey,
            req.ExpectedVersion), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateInspectionRecordFromTaskEndpoint(ISender sender)
    : QualityEndpoint<CreateInspectionRecordFromTaskRequest, ResponseData<CreateInspectionRecordFromTaskEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(
            QualityInspectionEndpointContracts.Get<CreateInspectionRecordFromTaskEndpoint>(),
            StatusCodes.Status403Forbidden,
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
