using FastEndpoints;
using FluentValidation;
using MediatR;
using Nerv.IIP.Business.Mes.Web.Application.Queries;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

public sealed record ListEngineeringChangeWorkOrderImpactsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ChangeNumber = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake,
    string? AuthorizedAssignedUserIds = null,
    string? AuthorizedTeamIds = null,
    string? AuthorizedWorkCenterIds = null);

public sealed class ListEngineeringChangeWorkOrderImpactsRequestValidator
    : Validator<ListEngineeringChangeWorkOrderImpactsRequest>
{
    public ListEngineeringChangeWorkOrderImpactsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).MaximumLength(100);
        RuleFor(x => x.WorkOrderId).MaximumLength(100);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.ChangeNumber) || !string.IsNullOrWhiteSpace(x.WorkOrderId))
            .WithMessage("请指定工程变更单号或工单标识。");
    }
}

public sealed class ListEngineeringChangeWorkOrderImpactsEndpoint(ISender sender)
    : MesEndpoint<ListEngineeringChangeWorkOrderImpactsRequest, ListEngineeringChangeWorkOrderImpactsResponse>
{
    public override void Configure() => ConfigureMesContract(
        MesEndpointContracts.Get<ListEngineeringChangeWorkOrderImpactsEndpoint>());

    public override async Task HandleAsync(ListEngineeringChangeWorkOrderImpactsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListEngineeringChangeWorkOrderImpactsQuery(
            req.OrganizationId, req.EnvironmentId, req.ChangeNumber, req.WorkOrderId, req.Skip, req.Take,
            req.AuthorizedAssignedUserIds, req.AuthorizedTeamIds, req.AuthorizedWorkCenterIds), ct);
        await Send.OkAsync(response, ct);
    }
}
