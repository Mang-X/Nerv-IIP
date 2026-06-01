using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;

public partial record MaintenanceInspectionId : IGuidStronglyTypedId;

public sealed class MaintenanceInspection : Entity<MaintenanceInspectionId>, IAggregateRoot
{
    private MaintenanceInspection()
    {
    }

    private MaintenanceInspection(
        string organizationId,
        string environmentId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc,
        MaintenancePlanId? planId,
        MaintenanceWorkOrderId? workOrderId)
    {
        if (planId is null && workOrderId is null)
        {
            throw new ArgumentException("Inspection must reference a maintenance plan or work order.");
        }

        Id = new MaintenanceInspectionId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        Inspector = MaintenanceText.Required(inspector, nameof(inspector));
        Result = MaintenanceText.Required(result, nameof(result));
        InspectedAtUtc = inspectedAtUtc.ToUniversalTime();
        PlanId = planId;
        WorkOrderId = workOrderId;
        this.AddDomainEvent(new MaintenanceInspectionRecordedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public MaintenancePlanId? PlanId { get; private set; }
    public MaintenanceWorkOrderId? WorkOrderId { get; private set; }
    public string Inspector { get; private set; } = string.Empty;
    public string Result { get; private set; } = string.Empty;
    public DateTimeOffset InspectedAtUtc { get; private set; }

    public static MaintenanceInspection Record(
        string organizationId,
        string environmentId,
        MaintenancePlanId? planId,
        MaintenanceWorkOrderId? workOrderId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc)
    {
        return new MaintenanceInspection(organizationId, environmentId, inspector, result, inspectedAtUtc, planId, workOrderId);
    }

    public static MaintenanceInspection RecordForPlan(
        string organizationId,
        string environmentId,
        MaintenancePlanId planId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc)
    {
        return new MaintenanceInspection(organizationId, environmentId, inspector, result, inspectedAtUtc, planId, null);
    }

    public static MaintenanceInspection RecordForWorkOrder(
        string organizationId,
        string environmentId,
        MaintenanceWorkOrderId workOrderId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc)
    {
        return new MaintenanceInspection(organizationId, environmentId, inspector, result, inspectedAtUtc, null, workOrderId);
    }
}
