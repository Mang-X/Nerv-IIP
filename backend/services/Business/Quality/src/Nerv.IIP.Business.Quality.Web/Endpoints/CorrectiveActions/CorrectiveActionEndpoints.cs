using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Queries.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.CorrectiveActions;

public sealed record OpenCorrectiveActionRequest(
    string OrganizationId,
    string EnvironmentId,
    string CapaCode,
    NonconformanceReportId? SourceNcrId,
    string RootCause,
    string ContainmentAction,
    string OwnerUserId,
    DateTimeOffset DueAtUtc);

public sealed record OpenCorrectiveActionResponse(CorrectiveActionId CorrectiveActionId);

public sealed record AddCorrectiveActionItemRequest(
    CorrectiveActionId CorrectiveActionId,
    string ActionType,
    string Description,
    string OwnerUserId,
    DateTimeOffset DueAtUtc);

public sealed record CompleteCorrectiveActionItemRequest(
    CorrectiveActionId CorrectiveActionId,
    CorrectiveActionItemId CorrectiveActionItemId,
    string CompletedByUserId,
    DateTimeOffset CompletedAtUtc);

public sealed record VerifyCorrectiveActionEffectivenessRequest(
    CorrectiveActionId CorrectiveActionId,
    string VerifiedByUserId,
    string Result,
    DateTimeOffset VerifiedAtUtc,
    InspectionRecordId? EffectivenessInspectionRecordId);

public sealed record CloseCorrectiveActionRequest(
    CorrectiveActionId CorrectiveActionId,
    string ClosedByUserId,
    string? CloseApprovalChainId);

public sealed record ListCorrectiveActionsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? OwnerUserId,
    string? SourceNcrId,
    bool? OverdueOnly,
    string? Keyword,
    int Skip = 0,
    int Take = 100);

public sealed record ListCorrectiveActionsEndpointResponse(
    IReadOnlyCollection<CorrectiveActionResponse> Items,
    int Total,
    int OpenCount,
    int EffectivenessVerifiedCount,
    int ClosedCount,
    int OverdueCount);

/// <summary>org/env 提供时按租户过滤（网关 facade 必传，越权与不存在同为 not found）。</summary>
public sealed record GetCorrectiveActionRequest(
    CorrectiveActionId CorrectiveActionId,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public sealed class ListCorrectiveActionsEndpoint(ISender sender)
    : QualityEndpoint<ListCorrectiveActionsRequest, ResponseData<ListCorrectiveActionsEndpointResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<ListCorrectiveActionsEndpoint>());
    }

    public override async Task HandleAsync(ListCorrectiveActionsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListCorrectiveActionsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.Status,
            req.OwnerUserId,
            req.SourceNcrId,
            req.OverdueOnly,
            req.Keyword,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(
            new ListCorrectiveActionsEndpointResponse(
                response.Items,
                response.Total,
                response.OpenCount,
                response.EffectivenessVerifiedCount,
                response.ClosedCount,
                response.OverdueCount).AsResponseData(),
            cancellation: ct);
    }
}

public sealed class GetCorrectiveActionEndpoint(ISender sender)
    : QualityEndpoint<GetCorrectiveActionRequest, ResponseData<CorrectiveActionResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<GetCorrectiveActionEndpoint>());
    }

    public override async Task HandleAsync(GetCorrectiveActionRequest req, CancellationToken ct)
    {
        var response = await sender.Send(
            new GetCorrectiveActionQuery(req.CorrectiveActionId, req.OrganizationId, req.EnvironmentId), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class OpenCorrectiveActionEndpoint(ISender sender)
    : QualityEndpoint<OpenCorrectiveActionRequest, ResponseData<OpenCorrectiveActionResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<OpenCorrectiveActionEndpoint>());
    }

    public override async Task HandleAsync(OpenCorrectiveActionRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new OpenCorrectiveActionCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.CapaCode,
            req.SourceNcrId,
            req.RootCause,
            req.ContainmentAction,
            req.OwnerUserId,
            req.DueAtUtc), ct);
        await Send.OkAsync(new OpenCorrectiveActionResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class AddCorrectiveActionItemEndpoint(ISender sender)
    : QualityEndpoint<AddCorrectiveActionItemRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<AddCorrectiveActionItemEndpoint>());
    }

    public override async Task HandleAsync(AddCorrectiveActionItemRequest req, CancellationToken ct)
    {
        await sender.Send(new AddCorrectiveActionItemCommand(
            req.CorrectiveActionId,
            req.ActionType,
            req.Description,
            req.OwnerUserId,
            req.DueAtUtc), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed class CompleteCorrectiveActionItemEndpoint(ISender sender)
    : QualityEndpoint<CompleteCorrectiveActionItemRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<CompleteCorrectiveActionItemEndpoint>());
    }

    public override async Task HandleAsync(CompleteCorrectiveActionItemRequest req, CancellationToken ct)
    {
        await sender.Send(new CompleteCorrectiveActionItemCommand(
            req.CorrectiveActionId,
            req.CorrectiveActionItemId,
            req.CompletedByUserId,
            req.CompletedAtUtc), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed class VerifyCorrectiveActionEffectivenessEndpoint(ISender sender)
    : QualityEndpoint<VerifyCorrectiveActionEffectivenessRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<VerifyCorrectiveActionEffectivenessEndpoint>());
    }

    public override async Task HandleAsync(VerifyCorrectiveActionEffectivenessRequest req, CancellationToken ct)
    {
        await sender.Send(new VerifyCorrectiveActionEffectivenessCommand(
            req.CorrectiveActionId,
            req.VerifiedByUserId,
            req.Result,
            req.VerifiedAtUtc,
            req.EffectivenessInspectionRecordId), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}

public sealed class CloseCorrectiveActionEndpoint(ISender sender)
    : QualityEndpoint<CloseCorrectiveActionRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure()
    {
        ConfigureQualityContract(QualityEndpointContracts.Get<CloseCorrectiveActionEndpoint>());
    }

    public override async Task HandleAsync(CloseCorrectiveActionRequest req, CancellationToken ct)
    {
        await sender.Send(new CloseCorrectiveActionCommand(req.CorrectiveActionId, req.ClosedByUserId, req.CloseApprovalChainId), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}
