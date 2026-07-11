namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

public partial record WarehouseTaskId : IGuidStronglyTypedId;

public enum WarehouseTaskType
{
    Putaway = 0,
    Picking = 1,
    Replenishment = 2,
}

public enum WarehouseTaskStatus
{
    Open = 0,
    Completed = 1,
    Cancelled = 2,
}

public sealed class WarehouseTask : Entity<WarehouseTaskId>, IAggregateRoot
{
    private WarehouseTask()
    {
    }

    private WarehouseTask(
        WarehouseTaskType taskType,
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string fromLocationCode,
        string toLocationCode,
        decimal plannedQuantity)
    {
        TaskType = taskType;
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        TaskNo = WmsText.Required(taskNo, nameof(taskNo));
        SourceOrderNo = WmsText.Required(sourceOrderNo, nameof(sourceOrderNo));
        SourceOrderLineNo = WmsText.Required(sourceOrderLineNo, nameof(sourceOrderLineNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        FromLocationCode = WmsText.Required(fromLocationCode, nameof(fromLocationCode));
        ToLocationCode = WmsText.Required(toLocationCode, nameof(toLocationCode));
        PlannedQuantity = WmsText.Positive(plannedQuantity, nameof(plannedQuantity));
        Status = WarehouseTaskStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public WarehouseTaskType TaskType { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string TaskNo { get; private set; } = string.Empty;
    public string SourceOrderNo { get; private set; } = string.Empty;
    public string SourceOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string FromLocationCode { get; private set; } = string.Empty;
    public string ToLocationCode { get; private set; } = string.Empty;
    public decimal PlannedQuantity { get; private set; }
    public decimal ExecutedQuantity { get; private set; }
    public WarehouseTaskStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static WarehouseTask CreatePutaway(
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string fromLocationCode,
        string toLocationCode,
        decimal plannedQuantity)
    {
        return new WarehouseTask(WarehouseTaskType.Putaway, organizationId, environmentId, taskNo, sourceOrderNo, sourceOrderLineNo, skuCode, uomCode, siteCode, fromLocationCode, toLocationCode, plannedQuantity);
    }

    public static WarehouseTask CreatePicking(
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string fromLocationCode,
        string toLocationCode,
        decimal plannedQuantity)
    {
        return new WarehouseTask(WarehouseTaskType.Picking, organizationId, environmentId, taskNo, sourceOrderNo, sourceOrderLineNo, skuCode, uomCode, siteCode, fromLocationCode, toLocationCode, plannedQuantity);
    }

    public static WarehouseTask CreateReplenishment(
        string organizationId,
        string environmentId,
        string taskNo,
        string sourceOrderNo,
        string sourceOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string toLocationCode,
        decimal plannedQuantity)
    {
        return new WarehouseTask(WarehouseTaskType.Replenishment, organizationId, environmentId, taskNo, sourceOrderNo, sourceOrderLineNo, skuCode, uomCode, siteCode, "REPLENISHMENT-SOURCE-PENDING", toLocationCode, plannedQuantity);
    }

    public void RecordProgress(decimal executedQuantity)
    {
        if (Status == WarehouseTaskStatus.Cancelled)
        {
            throw new InvalidOperationException("Cancelled warehouse tasks cannot record progress.");
        }

        if (Status == WarehouseTaskStatus.Completed)
        {
            throw new InvalidOperationException("Completed warehouse tasks cannot record progress.");
        }

        if (executedQuantity < 0 || executedQuantity > PlannedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(executedQuantity), executedQuantity, "Executed quantity must be within planned quantity.");
        }

        if (executedQuantity < ExecutedQuantity)
        {
            throw new InvalidOperationException("Warehouse task progress cannot regress.");
        }

        ExecutedQuantity = executedQuantity;
        if (ExecutedQuantity == PlannedQuantity)
        {
            Status = WarehouseTaskStatus.Completed;
            CompletedAtUtc = DateTime.UtcNow;
        }
    }

    public void Cancel()
    {
        if (Status == WarehouseTaskStatus.Completed)
        {
            throw new InvalidOperationException("Completed warehouse tasks cannot be cancelled.");
        }

        Status = WarehouseTaskStatus.Cancelled;
    }
}
