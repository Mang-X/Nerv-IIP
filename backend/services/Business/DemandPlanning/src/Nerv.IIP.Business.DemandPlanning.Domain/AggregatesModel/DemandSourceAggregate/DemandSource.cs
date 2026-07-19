using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;

public partial record DemandSourceId : IGuidStronglyTypedId;

public sealed class DemandSource : Entity<DemandSourceId>, IAggregateRoot
{
    private DemandSource()
    {
    }

    private DemandSource(
        string organizationId,
        string environmentId,
        string demandType,
        string sourceReference,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly dueDate)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        DemandType = NormalizeDemandType(demandType);
        SourceReference = DemandPlanningText.Required(sourceReference, nameof(sourceReference));
        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        SourceStatus = "active";
        DueDate = dueDate;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new DemandSourceCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DemandType { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string SourceLineReference { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public int SourceVersion { get; private set; }
    public string SourceStatus { get; private set; } = "active";
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public DateOnly DueDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static DemandSource Create(
        string organizationId,
        string environmentId,
        string demandType,
        string sourceReference,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly dueDate)
    {
        if (IsSalesOrderDemandType(demandType))
        {
            throw new InvalidOperationException("Demand type 'sales-order' is integration-owned and cannot be created manually.");
        }

        return new DemandSource(organizationId, environmentId, demandType, sourceReference, skuCode, uomCode, siteCode, quantity, dueDate);
    }

    public static string NormalizeDemandType(string demandType) =>
        DemandPlanningText.Required(demandType, nameof(demandType)).ToLowerInvariant();

    public static bool IsSalesOrderDemandType(string demandType) =>
        string.Equals(NormalizeDemandType(demandType), "sales-order", StringComparison.Ordinal);

    public static DemandSource CreateSalesOrderDemand(
        string organizationId,
        string environmentId,
        string sourceDocumentId,
        string salesOrderNo,
        string salesOrderLineNo,
        string customerCode,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly dueDate,
        int sourceVersion)
    {
        if (sourceVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVersion), sourceVersion, "Sales order source version must be positive.");
        }

        var demand = new DemandSource(
            organizationId,
            environmentId,
            "sales-order",
            salesOrderNo,
            skuCode,
            uomCode,
            siteCode,
            quantity,
            dueDate)
        {
            SourceDocumentId = DemandPlanningText.Required(sourceDocumentId, nameof(sourceDocumentId)),
            SourceLineReference = DemandPlanningText.Required(salesOrderLineNo, nameof(salesOrderLineNo)),
            CustomerCode = DemandPlanningText.Required(customerCode, nameof(customerCode)),
            SourceVersion = sourceVersion,
        };
        return demand;
    }

    public void Update(decimal quantity, DateOnly dueDate)
    {
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        DueDate = dueDate;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool ApplySalesOrderSnapshot(decimal quantity, DateOnly dueDate, int sourceVersion)
    {
        if (sourceVersion <= SourceVersion)
        {
            return false;
        }

        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        DueDate = dueDate;
        SourceVersion = sourceVersion;
        SourceStatus = "active";
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    public bool CancelFromSalesOrder(int sourceVersion)
    {
        if (sourceVersion <= SourceVersion)
        {
            return false;
        }

        Quantity = 0m;
        SourceVersion = sourceVersion;
        SourceStatus = "cancelled";
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return true;
    }
}
