using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Contracts.Erp;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

public sealed class WorkOrderCostCompletedIntegrationEventConverter
    : IIntegrationEventConverter<WorkOrderCostCompletedDomainEvent, WorkOrderCostCapitalizedIntegrationEvent>
{
    public WorkOrderCostCapitalizedIntegrationEvent Convert(WorkOrderCostCompletedDomainEvent domainEvent)
    {
        var cost = domainEvent.WorkOrderCost;
        var completedAtUtc = cost.CompletedAtUtc ?? throw new InvalidOperationException("Completed work-order cost requires a completion timestamp.");
        var unitCost = cost.TotalAccumulatedCost / cost.CompletedQuantity;
        return new WorkOrderCostCapitalizedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            ErpIntegrationEventTypes.WorkOrderCostCapitalized,
            ErpIntegrationEventVersions.V1,
            completedAtUtc,
            ErpIntegrationEventSources.BusinessErp,
            cost.WorkOrderId,
            cost.WorkOrderId,
            cost.OrganizationId,
            cost.EnvironmentId,
            "system:erp",
            $"work-order-cost-capitalized:{cost.OrganizationId}:{cost.EnvironmentId}:{cost.WorkOrderId}",
            new WorkOrderCostCapitalizedPayload(
                cost.WorkOrderId,
                cost.SkuCode,
                cost.CompletedQuantity,
                cost.MaterialCost,
                cost.LaborCost,
                cost.TotalAccumulatedCost,
                unitCost,
                completedAtUtc));
    }
}
