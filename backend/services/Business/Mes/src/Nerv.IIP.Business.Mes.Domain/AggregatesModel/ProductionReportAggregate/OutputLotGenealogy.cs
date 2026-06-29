namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record OutputLotGenealogyId : IGuidStronglyTypedId;

public sealed class OutputLotGenealogy : Entity<OutputLotGenealogyId>, IAggregateRoot
{
    private OutputLotGenealogy()
    {
    }

    private OutputLotGenealogy(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string reportNo,
        string producedLotNo,
        string? serialNo,
        decimal quantity,
        DateTimeOffset createdAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        ReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
        ProducedLotNo = DomainGuard.Required(producedLotNo, nameof(producedLotNo));
        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        CreatedAtUtc = createdAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string ReportNo { get; private set; } = string.Empty;
    public string ProducedLotNo { get; private set; } = string.Empty;
    public string? SerialNo { get; private set; }
    public decimal Quantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static OutputLotGenealogy Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string reportNo,
        string producedLotNo,
        string? serialNo,
        decimal quantity,
        DateTimeOffset createdAtUtc)
    {
        return new OutputLotGenealogy(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            reportNo,
            producedLotNo,
            serialNo,
            quantity,
            createdAtUtc);
    }
}
