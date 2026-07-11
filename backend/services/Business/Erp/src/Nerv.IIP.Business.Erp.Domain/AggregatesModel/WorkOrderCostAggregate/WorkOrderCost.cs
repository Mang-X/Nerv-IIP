using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public partial record WorkOrderCostId : IGuidStronglyTypedId;
public partial record WorkOrderCostDetailId : IGuidStronglyTypedId;

public enum WorkOrderCostDetailType { Labor, Material }

public sealed class WorkOrderCost : Entity<WorkOrderCostId>, IAggregateRoot
{
    private readonly List<WorkOrderCostDetail> details = [];
    private WorkOrderCost() { }
    private WorkOrderCost(string organizationId, string environmentId, string workOrderId, string skuCode)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkOrderId = ErpText.Required(workOrderId, nameof(workOrderId));
        SkuCode = ErpText.Required(skuCode, nameof(skuCode));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public decimal CompletedQuantity { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int ExpectedReportCount { get; private set; }
    public int ReceivedReportCount { get; private set; }
    public int ExpectedMaterialMovementCount { get; private set; }
    public int ReceivedMaterialMovementCount { get; private set; }
    public bool CapitalizationPublished { get; private set; }
    public decimal CapitalizedQuantity { get; private set; }
    public decimal WipClearedCost { get; private set; }
    public decimal CapitalizedCost { get; private set; }
    public decimal VarianceCost => TotalAccumulatedCost - CapitalizedCost;
    public decimal LaborCost => details.Where(x => x.Type == WorkOrderCostDetailType.Labor).Sum(x => x.Amount);
    public decimal MaterialCost => details.Where(x => x.Type == WorkOrderCostDetailType.Material).Sum(x => x.Amount);
    public decimal TotalAccumulatedCost => LaborCost + MaterialCost;
    public bool IsReconciled => CompletedAtUtc.HasValue && TotalAccumulatedCost == CapitalizedCost + VarianceCost;
    public IReadOnlyCollection<WorkOrderCostDetail> Details => details;

    public static WorkOrderCost Open(string organizationId, string environmentId, string workOrderId, string skuCode)
        => new(organizationId, environmentId, workOrderId, skuCode);

    public void AssignSku(string skuCode) => SkuCode = ErpText.Required(skuCode, nameof(skuCode));

    public void RecordLabor(string sourceDocumentId, string workCenterId, decimal hours, decimal hourlyRate, bool isReversal, DateTimeOffset occurredAtUtc)
    {
        ErpText.Positive(hours, nameof(hours));
        ErpText.Positive(hourlyRate, nameof(hourlyRate));
        details.Add(WorkOrderCostDetail.Create(WorkOrderCostDetailType.Labor, sourceDocumentId, workCenterId, hours, hourlyRate, isReversal ? -(hours * hourlyRate) : hours * hourlyRate, occurredAtUtc));
        if (!isReversal) ReceivedReportCount++;
        TryPublishCapitalization();
    }

    public void ExpectMaterialMovements(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        ExpectedMaterialMovementCount += count;
        TryPublishCapitalization();
    }

    public void RecordMaterial(string sourceDocumentId, string reportNo, string skuCode, decimal signedQuantity, decimal unitCost, DateTimeOffset occurredAtUtc)
    {
        if (signedQuantity == 0m) throw new ArgumentOutOfRangeException(nameof(signedQuantity));
        ErpText.Positive(unitCost, nameof(unitCost));
        details.Add(WorkOrderCostDetail.Create(WorkOrderCostDetailType.Material, sourceDocumentId, skuCode, signedQuantity, unitCost, signedQuantity * unitCost, occurredAtUtc, reportNo));
        if (signedQuantity > 0m) ReceivedMaterialMovementCount++;
        TryPublishCapitalization();
    }

    public void Complete(decimal completedQuantity, int expectedReportCount, int expectedMaterialMovementCount, DateTimeOffset completedAtUtc)
    {
        CompletedQuantity = ErpText.Positive(completedQuantity, nameof(completedQuantity));
        if (expectedReportCount <= 0) throw new ArgumentOutOfRangeException(nameof(expectedReportCount));
        if (expectedMaterialMovementCount < 0) throw new ArgumentOutOfRangeException(nameof(expectedMaterialMovementCount));
        ExpectedReportCount = expectedReportCount;
        ExpectedMaterialMovementCount = expectedMaterialMovementCount;
        CompletedAtUtc = completedAtUtc;
        TryPublishCapitalization();
    }

    public void Capitalize(string sourceDocumentId, decimal quantity, decimal unitCost, DateTimeOffset occurredAtUtc)
    {
        _ = ErpText.Required(sourceDocumentId, nameof(sourceDocumentId));
        ErpText.Positive(quantity, nameof(quantity));
        ErpText.Positive(unitCost, nameof(unitCost));
        CapitalizedQuantity += quantity;
        CapitalizedCost += quantity * unitCost;
    }

    public void RecordWipClearance(decimal amount)
    {
        if (amount == 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        WipClearedCost += amount;
    }

    private void TryPublishCapitalization()
    {
        if (!CapitalizationPublished && CompletedAtUtc.HasValue && ReceivedReportCount >= ExpectedReportCount && ReceivedMaterialMovementCount >= ExpectedMaterialMovementCount)
        {
            CapitalizationPublished = true;
            AddDomainEvent(new WorkOrderCostCompletedDomainEvent(this));
        }
    }
}

public partial record WorkCenterCostRateId : IGuidStronglyTypedId;

public sealed class WorkCenterCostRate : Entity<WorkCenterCostRateId>, IAggregateRoot
{
    private WorkCenterCostRate() { }
    private WorkCenterCostRate(string organizationId, string environmentId, string workCenterId, decimal hourlyRate)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkCenterId = ErpText.Required(workCenterId, nameof(workCenterId));
        HourlyRate = ErpText.Positive(hourlyRate, nameof(hourlyRate));
    }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public decimal HourlyRate { get; private set; }
    public static WorkCenterCostRate Define(string organizationId, string environmentId, string workCenterId, decimal hourlyRate)
        => new(organizationId, environmentId, workCenterId, hourlyRate);
}

public partial record PendingMaterialCostId : IGuidStronglyTypedId;
public sealed class PendingMaterialCost : Entity<PendingMaterialCostId>, IAggregateRoot
{
    private PendingMaterialCost() { }
    private PendingMaterialCost(string organizationId, string environmentId, string movementId, string reportNo, string skuCode, decimal signedQuantity, decimal unitCost, DateTimeOffset postedAtUtc)
    { OrganizationId = organizationId; EnvironmentId = environmentId; MovementId = movementId; ReportNo = reportNo; SkuCode = skuCode; SignedQuantity = signedQuantity; UnitCost = unitCost; PostedAtUtc = postedAtUtc; }
    public string OrganizationId { get; private set; } = string.Empty; public string EnvironmentId { get; private set; } = string.Empty;
    public string MovementId { get; private set; } = string.Empty; public string ReportNo { get; private set; } = string.Empty; public string SkuCode { get; private set; } = string.Empty;
    public decimal SignedQuantity { get; private set; } public decimal UnitCost { get; private set; } public DateTimeOffset PostedAtUtc { get; private set; }
    public static PendingMaterialCost Create(string organizationId, string environmentId, string movementId, string reportNo, string skuCode, decimal signedQuantity, decimal unitCost, DateTimeOffset postedAtUtc)
        => new(organizationId, environmentId, movementId, reportNo, skuCode, signedQuantity, unitCost, postedAtUtc);
}

public sealed class WorkOrderCostDetail : Entity<WorkOrderCostDetailId>
{
    private WorkOrderCostDetail() { }
    private WorkOrderCostDetail(WorkOrderCostDetailType type, string sourceDocumentId, string dimensionCode, decimal quantity, decimal rate, decimal amount, DateTimeOffset occurredAtUtc, string? reportNo)
    {
        Type = type; SourceDocumentId = ErpText.Required(sourceDocumentId, nameof(sourceDocumentId));
        DimensionCode = ErpText.Required(dimensionCode, nameof(dimensionCode)); Quantity = quantity; Rate = rate; Amount = amount;
        OccurredAtUtc = occurredAtUtc; ReportNo = reportNo;
    }
    public WorkOrderCostDetailType Type { get; private set; }
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string DimensionCode { get; private set; } = string.Empty;
    public string? ReportNo { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    internal static WorkOrderCostDetail Create(WorkOrderCostDetailType type, string sourceDocumentId, string dimensionCode, decimal quantity, decimal rate, decimal amount, DateTimeOffset occurredAtUtc, string? reportNo = null)
        => new(type, sourceDocumentId, dimensionCode, quantity, rate, amount, occurredAtUtc, reportNo);
}
