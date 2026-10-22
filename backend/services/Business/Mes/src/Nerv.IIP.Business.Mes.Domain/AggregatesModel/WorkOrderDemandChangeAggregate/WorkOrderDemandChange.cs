namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderDemandChangeAggregate;

public partial record WorkOrderDemandChangeId : IGuidStronglyTypedId;

public sealed class WorkOrderDemandChange : Entity<WorkOrderDemandChangeId>, IAggregateRoot
{
    private WorkOrderDemandChange()
    {
    }

    public WorkOrderDemandChange(
        string organizationId,
        string environmentId,
        string workOrderId,
        string suggestionId,
        string demandSourceReference,
        string salesOrderId,
        int orderVersion,
        bool cancelled)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        SuggestionId = DomainGuard.Required(suggestionId, nameof(suggestionId));
        DemandSourceReference = DomainGuard.Required(demandSourceReference, nameof(demandSourceReference));
        SalesOrderId = DomainGuard.Required(salesOrderId, nameof(salesOrderId));
        OrderVersion = orderVersion;
        Cancelled = cancelled;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SuggestionId { get; private set; } = string.Empty;
    public string DemandSourceReference { get; private set; } = string.Empty;
    public string SalesOrderId { get; private set; } = string.Empty;
    public int OrderVersion { get; private set; }
    public bool Cancelled { get; private set; }

    public void ApplyNewerVersion(int orderVersion, bool cancelled)
    {
        if (orderVersion <= OrderVersion)
        {
            return;
        }

        OrderVersion = orderVersion;
        Cancelled = cancelled;
    }
}
