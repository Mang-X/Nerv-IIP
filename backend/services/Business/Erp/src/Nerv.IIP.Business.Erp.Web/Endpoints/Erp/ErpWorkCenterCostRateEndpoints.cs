using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record ConfigureWorkCenterCostRateRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    decimal HourlyRate,
    string CurrencyCode,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string Reason);

public sealed record ConfigureWorkCenterCostRateResponse(WorkCenterCostRateId WorkCenterCostRateId);

public sealed record ListWorkCenterCostRatesRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    DateTimeOffset? AtUtc = null);

public sealed class ConfigureWorkCenterCostRateEndpoint(
    ISender sender,
    IErpIntegrationEventContextAccessor eventContext,
    TimeProvider timeProvider)
    : ErpEndpoint<ConfigureWorkCenterCostRateRequest, ResponseData<ConfigureWorkCenterCostRateResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ConfigureWorkCenterCostRateEndpoint>());

    public override async Task HandleAsync(ConfigureWorkCenterCostRateRequest req, CancellationToken ct)
    {
        using var causationScope = eventContext.BeginScope(ErpCommandCausationIds.ForHttpCommand(
            "configure-work-center-cost-rate",
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkCenterId,
            req.HourlyRate,
            req.CurrencyCode,
            req.EffectiveFromUtc,
            req.EffectiveToUtc,
            req.Reason));
        var actor = eventContext.GetContext().Actor;
        var id = await sender.Send(new ConfigureWorkCenterCostRateCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkCenterId,
            req.HourlyRate,
            req.CurrencyCode,
            req.EffectiveFromUtc,
            req.EffectiveToUtc,
            actor,
            req.Reason,
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(new ConfigureWorkCenterCostRateResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWorkCenterCostRatesEndpoint(ISender sender)
    : ErpEndpoint<ListWorkCenterCostRatesRequest, ResponseData<ListWorkCenterCostRatesResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListWorkCenterCostRatesEndpoint>());

    public override async Task HandleAsync(ListWorkCenterCostRatesRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWorkCenterCostRatesQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkCenterId,
            req.AtUtc), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
