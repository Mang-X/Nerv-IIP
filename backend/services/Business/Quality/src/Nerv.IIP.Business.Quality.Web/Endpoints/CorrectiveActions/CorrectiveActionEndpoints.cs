using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
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

public sealed record VerifyCorrectiveActionEffectivenessRequest(
    CorrectiveActionId CorrectiveActionId,
    string VerifiedByUserId,
    string Result,
    DateTimeOffset VerifiedAtUtc);

public sealed record CloseCorrectiveActionRequest(CorrectiveActionId CorrectiveActionId, string ClosedByUserId);

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
            req.VerifiedAtUtc), ct);
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
        await sender.Send(new CloseCorrectiveActionCommand(req.CorrectiveActionId, req.ClosedByUserId), ct);
        await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct);
    }
}
