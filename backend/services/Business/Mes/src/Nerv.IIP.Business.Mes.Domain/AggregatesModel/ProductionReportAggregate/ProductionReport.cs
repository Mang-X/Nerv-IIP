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
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        WorkOrderId = Required(workOrderId);
        OperationTaskId = Required(operationTaskId);
        GoodQuantity = NonNegative(goodQuantity, nameof(goodQuantity));
        ScrapQuantity = NonNegative(scrapQuantity, nameof(scrapQuantity));
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

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static decimal NonNegative(decimal value, string parameterName)
    {
        return value >= 0 ? value : throw new ArgumentOutOfRangeException(parameterName, "Quantity cannot be negative.");
    }
}
