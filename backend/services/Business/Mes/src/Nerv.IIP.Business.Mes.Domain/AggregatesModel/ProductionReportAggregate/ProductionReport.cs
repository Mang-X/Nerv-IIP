namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportId : IGuidStronglyTypedId;

public sealed class ProductionReport : Entity<ProductionReportId>, IAggregateRoot
{
    private ProductionReport()
    {
    }

    private ProductionReport(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        decimal goodQuantity,
        decimal scrapQuantity,
        bool completesOperation,
        DateTimeOffset reportedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        GoodQuantity = DomainGuard.NonNegative(goodQuantity, nameof(goodQuantity));
        ScrapQuantity = DomainGuard.NonNegative(scrapQuantity, nameof(scrapQuantity));
        if (GoodQuantity + ScrapQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "At least one reported quantity must be positive.");
        }

        CompletesOperation = completesOperation;
        ReportedAtUtc = reportedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public decimal GoodQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public bool CompletesOperation { get; private set; }
    public DateTimeOffset ReportedAtUtc { get; private set; }

    public static ProductionReport Record(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        decimal goodQuantity,
        decimal scrapQuantity,
        bool completesOperation,
        DateTimeOffset reportedAtUtc)
    {
        return new ProductionReport(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            goodQuantity,
            scrapQuantity,
            completesOperation,
            reportedAtUtc);
    }

}
