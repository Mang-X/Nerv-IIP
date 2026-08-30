using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record GetWorkOrderCostVarianceRequest(
    string WorkOrderId,
    int PageNumber = 1,
    int PageSize = 50);

public sealed class GetWorkOrderCostVarianceEndpoint(
    ISender sender,
    IErpMachineOverheadInternalScopeAuthorizer scopeAuthorizer)
    : ErpEndpoint<GetWorkOrderCostVarianceRequest, ResponseData<WorkOrderCostVarianceResponse>>
{
    public override void Configure()
        => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetWorkOrderCostVarianceEndpoint>());

    public override async Task HandleAsync(GetWorkOrderCostVarianceRequest req, CancellationToken ct)
    {
        var authorization = scopeAuthorizer.ResolveAuthorizedScope(HttpContext);
        if (authorization is ErpInternalServiceScopeAuthorization.MissingRequiredHeader)
        {
            await ErpMachineOverheadEndpointResults.WriteMissingScopeHeadersAsync(HttpContext, ct);
            return;
        }
        if (authorization is ErpInternalServiceScopeAuthorization.Forbidden)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var scope = ((ErpInternalServiceScopeAuthorization.Authorized)authorization).Scope;
        var response = await sender.Send(new GetWorkOrderCostVarianceQuery(
            scope.OrganizationId,
            scope.EnvironmentId,
            req.WorkOrderId,
            req.PageNumber,
            req.PageSize), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record ListWorkOrderCostsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId = null,
    string? SourceNcrId = null,
    string? SourceWorkOrderId = null,
    int Skip = 0,
    int Take = 100);

public sealed class ListWorkOrderCostsEndpoint(ISender sender)
    : ErpEndpoint<ListWorkOrderCostsRequest, ResponseData<ListWorkOrderCostsResponse>>
{
    public override void Configure() =>
        ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListWorkOrderCostsEndpoint>());

    public override async Task HandleAsync(ListWorkOrderCostsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWorkOrderCostsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkOrderId,
            req.SourceNcrId,
            req.SourceWorkOrderId,
            req.Skip,
            req.Take), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
