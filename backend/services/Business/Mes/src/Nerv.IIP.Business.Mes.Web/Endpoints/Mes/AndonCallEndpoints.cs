using FastEndpoints;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Andon;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Andon;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

public class AndonCallScopeRequest
{
    public string OrganizationId { get; init; } = string.Empty;
    public string EnvironmentId { get; init; } = string.Empty;
    public string? AssignedUserIds { get; init; }
    public string? TeamIds { get; init; }
    public string? WorkCenterIds { get; init; }
    public AndonCallScope ToScope() => new(OrganizationId.Trim(), EnvironmentId.Trim(), AssignedUserIds, TeamIds, WorkCenterIds);
}
public sealed class RaiseAndonCallRequest : AndonCallScopeRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter<AndonCallCategory>))]
    public AndonCallCategory Category { get; init; }
    public string WorkOrderId { get; init; } = string.Empty;
    public string OperationTaskId { get; init; } = string.Empty;
    public string WorkCenterId { get; init; } = string.Empty;
}
public class GetAndonCallRequest : AndonCallScopeRequest
{
    [RouteParam] public Guid Id { get; init; }
}
public sealed class AndonCallActionRequest : GetAndonCallRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
}
public sealed class ListAndonCallsRequest : AndonCallScopeRequest
{
    public AndonCallQueue Queue { get; init; } = AndonCallQueue.Unclosed;
    public AndonCallCategory? Category { get; init; }
    public string? WorkCenterId { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}
public sealed class RaiseAndonCallRequestValidator : Validator<RaiseAndonCallRequest>
{
    public RaiseAndonCallRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Category).IsInEnum();
    }
}
public sealed class AndonCallActionRequestValidator : Validator<AndonCallActionRequest>
{
    public AndonCallActionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}
public sealed class RaiseAndonCallEndpoint(ISender sender) : MesEndpoint<RaiseAndonCallRequest, AndonCallResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<RaiseAndonCallEndpoint>(), 409);
    public override async Task HandleAsync(RaiseAndonCallRequest req, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new RaiseAndonCallCommand(req.ToScope(), req.IdempotencyKey.Trim(),
            req.Category, req.WorkOrderId.Trim(), req.OperationTaskId.Trim(), req.WorkCenterId.Trim(), MesAuthenticatedActor.Resolve(HttpContext)), ct), ct);
}
public sealed class ClaimAndonCallEndpoint(ISender sender) : MesEndpoint<AndonCallActionRequest, AndonCallResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ClaimAndonCallEndpoint>(), 409);
    public override async Task HandleAsync(AndonCallActionRequest req, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new ClaimAndonCallCommand(req.ToScope(), new AndonCallId(req.Id), req.IdempotencyKey.Trim(),
            MesAuthenticatedActor.Resolve(HttpContext)), ct), ct);
}
public sealed class CloseAndonCallEndpoint(ISender sender) : MesEndpoint<AndonCallActionRequest, AndonCallResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<CloseAndonCallEndpoint>(), 409);
    public override async Task HandleAsync(AndonCallActionRequest req, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new CloseAndonCallCommand(req.ToScope(), new AndonCallId(req.Id), req.IdempotencyKey.Trim(),
            MesAuthenticatedActor.Resolve(HttpContext)), ct), ct);
}
public sealed class GetAndonCallEndpoint(ISender sender) : MesEndpoint<GetAndonCallRequest, AndonCallResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<GetAndonCallEndpoint>());
    public override async Task HandleAsync(GetAndonCallRequest req, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetAndonCallQuery(req.ToScope(), new AndonCallId(req.Id)), ct), ct);
}
public sealed class ListAndonCallsEndpoint(ISender sender) : MesEndpoint<ListAndonCallsRequest, AndonCallListResponse>
{
    public override void Configure() => ConfigureMesContract(MesEndpointContracts.Get<ListAndonCallsEndpoint>());
    public override async Task HandleAsync(ListAndonCallsRequest req, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new ListAndonCallsQuery(req.ToScope(), req.Queue, req.Category,
            req.WorkCenterId, req.Skip, req.Take), ct), ct);
}
