using FastEndpoints;
using MediatR;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

public sealed record WorkOrderTransformationTargetRequest(string WorkOrderId, decimal Quantity);

public sealed record SplitWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string WorkOrderId,
    IReadOnlyCollection<WorkOrderTransformationTargetRequest> Targets,
    string Reason,
    string IdempotencyKey);

public sealed record MergeWorkOrdersRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> SourceWorkOrderIds,
    string TargetWorkOrderId,
    string Reason,
    string IdempotencyKey);

public sealed record GetWorkOrderTransformationRequest(
    string OrganizationId,
    string EnvironmentId,
    [property: RouteParam] string TransformationId);

public sealed class SplitWorkOrderEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<SplitWorkOrderRequest, WorkOrderTransformationResult>
{
    public override void Configure() => ConfigureMesContract(
        MesEndpointContracts.Get<SplitWorkOrderEndpoint>(),
        StatusCodes.Status409Conflict);

    public override async Task HandleAsync(SplitWorkOrderRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new SplitWorkOrderCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.Targets.Select(x => new WorkOrderTransformationTarget(x.WorkOrderId, x.Quantity)).ToArray(),
            req.Reason,
            req.IdempotencyKey,
            MesAuthenticatedActor.Resolve(HttpContext),
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class MergeWorkOrdersEndpoint(ISender sender, TimeProvider timeProvider)
    : MesEndpoint<MergeWorkOrdersRequest, WorkOrderTransformationResult>
{
    public override void Configure() => ConfigureMesContract(
        MesEndpointContracts.Get<MergeWorkOrdersEndpoint>(),
        StatusCodes.Status409Conflict);

    public override async Task HandleAsync(MergeWorkOrdersRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new MergeWorkOrdersCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.SourceWorkOrderIds,
            req.TargetWorkOrderId,
            req.Reason,
            req.IdempotencyKey,
            MesAuthenticatedActor.Resolve(HttpContext),
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(response, ct);
    }
}

public sealed class GetWorkOrderTransformationEndpoint(ISender sender)
    : MesEndpoint<GetWorkOrderTransformationRequest, WorkOrderTransformationReadback>
{
    public override void Configure() => ConfigureMesContract(
        MesEndpointContracts.Get<GetWorkOrderTransformationEndpoint>());

    public override async Task HandleAsync(GetWorkOrderTransformationRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TransformationId, out var transformationId))
        {
            throw new KnownException("工单转换记录标识无效。");
        }

        var response = await sender.Send(new GetWorkOrderTransformationQuery(
            req.OrganizationId,
            req.EnvironmentId,
            new WorkOrderTransformationId(transformationId)), ct);
        await Send.OkAsync(response, ct);
    }
}
