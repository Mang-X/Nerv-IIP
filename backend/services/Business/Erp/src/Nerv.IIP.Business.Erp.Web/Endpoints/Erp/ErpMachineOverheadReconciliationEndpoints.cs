using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record ReconcileWorkCenterMachineOverheadRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode,
    decimal ActualFixedOverheadAmount,
    decimal ActualVariableOverheadAmount,
    string CurrencyCode,
    long AbnormalDowntimeTicks,
    AbnormalDowntimeDisposition AbnormalDowntimeDisposition,
    string SourceReference,
    string Reason);

public sealed record ReconcileWorkCenterMachineOverheadResponse(
    WorkCenterMachineOverheadReconciliationId WorkCenterMachineOverheadReconciliationId);

public sealed record ListWorkCenterMachineOverheadReconciliationsRequest(
    string OrganizationId,
    string EnvironmentId,
    string AccountingPeriodCode,
    string? WorkCenterId = null,
    int PageNumber = 1,
    int PageSize = 50);

public sealed class ReconcileWorkCenterMachineOverheadEndpoint(
    ISender sender,
    IErpIntegrationEventContextAccessor eventContext,
    TimeProvider timeProvider)
    : ErpEndpoint<ReconcileWorkCenterMachineOverheadRequest, ResponseData<ReconcileWorkCenterMachineOverheadResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ReconcileWorkCenterMachineOverheadEndpoint>());

    public override async Task HandleAsync(ReconcileWorkCenterMachineOverheadRequest req, CancellationToken ct)
    {
        using var causationScope = eventContext.BeginScope(ErpCommandCausationIds.ForHttpCommand(
            "reconcile-work-center-machine-overhead",
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkCenterId,
            req.AccountingPeriodCode,
            req.ActualFixedOverheadAmount,
            req.ActualVariableOverheadAmount,
            req.CurrencyCode,
            req.AbnormalDowntimeTicks,
            req.AbnormalDowntimeDisposition,
            req.SourceReference,
            req.Reason));
        var id = await sender.Send(new ReconcileWorkCenterMachineOverheadCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.WorkCenterId,
            req.AccountingPeriodCode,
            req.ActualFixedOverheadAmount,
            req.ActualVariableOverheadAmount,
            req.CurrencyCode,
            req.AbnormalDowntimeTicks,
            req.AbnormalDowntimeDisposition,
            eventContext.GetContext().Actor,
            req.SourceReference,
            req.Reason,
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(new ReconcileWorkCenterMachineOverheadResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWorkCenterMachineOverheadReconciliationsEndpoint(ISender sender)
    : ErpEndpoint<ListWorkCenterMachineOverheadReconciliationsRequest, ResponseData<ListWorkCenterMachineOverheadReconciliationsResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListWorkCenterMachineOverheadReconciliationsEndpoint>());

    public override async Task HandleAsync(ListWorkCenterMachineOverheadReconciliationsRequest req, CancellationToken ct)
    {
        var response = await sender.Send(new ListWorkCenterMachineOverheadReconciliationsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.AccountingPeriodCode,
            req.WorkCenterId,
            req.PageNumber,
            req.PageSize), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}
