namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportSerialNumberId : IGuidStronglyTypedId;

public static class ProductionSerialTrackingPolicies
{
    public const string None = "none";
    public const string OnReceipt = "on-receipt";
    public const string OnProduction = "on-production";
    public const string OnShipment = "on-shipment";

    public static bool IsSupported(string value) => value is None or OnReceipt or OnProduction or OnShipment;
}

public sealed class ProductionReportSerialNumberAssignment
{
    private ProductionReportSerialNumberAssignment(string serialTrackingPolicy, IReadOnlyList<string> serialNumbers)
    {
        SerialTrackingPolicy = serialTrackingPolicy;
        SerialNumbers = serialNumbers;
    }

    public string SerialTrackingPolicy { get; }

    public IReadOnlyList<string> SerialNumbers { get; }

    public static ProductionReportSerialNumberAssignment Create(
        string serialTrackingPolicy,
        decimal goodQuantity,
        IReadOnlyCollection<string>? serialNumbers,
        string? legacySerialNumber = null)
    {
        if (string.IsNullOrWhiteSpace(serialTrackingPolicy))
        {
            throw new InvalidOperationException("Serial tracking policy is required.");
        }

        var policy = serialTrackingPolicy.Trim();
        if (!ProductionSerialTrackingPolicies.IsSupported(policy))
        {
            throw new InvalidOperationException($"Unsupported serial tracking policy: {policy}.");
        }

        IReadOnlyCollection<string> inputs = serialNumbers ?? [];
        if (!string.IsNullOrWhiteSpace(legacySerialNumber))
        {
            if (serialNumbers is not null || policy != ProductionSerialTrackingPolicies.None)
            {
                throw new InvalidOperationException(
                    "Legacy serialNo cannot be combined with a non-default serialTrackingPolicy or serialNumbers.");
            }

            policy = ProductionSerialTrackingPolicies.OnProduction;
            inputs = [legacySerialNumber];
        }

        if (policy != ProductionSerialTrackingPolicies.OnProduction)
        {
            if (inputs.Count > 0)
            {
                throw new InvalidOperationException($"Production serial numbers are not accepted for policy {policy}.");
            }

            return new ProductionReportSerialNumberAssignment(policy, []);
        }

        if (decimal.Truncate(goodQuantity) != goodQuantity)
        {
            throw new InvalidOperationException("Good quantity must be an integer when serial tracking policy is on-production.");
        }

        IReadOnlyList<string> normalized;
        try
        {
            normalized = ProductionReportSerialNumber.Normalize(inputs.ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }

        if (normalized.Count != goodQuantity)
        {
            throw new InvalidOperationException(
                $"Production serial number count {normalized.Count} must equal good quantity {goodQuantity}.");
        }

        return new ProductionReportSerialNumberAssignment(policy, normalized);
    }

    public IReadOnlyList<ProductionReportSerialNumber> CreateFacts(ProductionReport report)
    {
        return ProductionReportSerialNumber.CreateForNormalizedReport(report, SerialNumbers);
    }
}

public sealed class ProductionReportSerialNumber : Entity<ProductionReportSerialNumberId>
{
    public const int SerialNumberMaxLength = 150;

    private ProductionReportSerialNumber()
    {
    }

    private ProductionReportSerialNumber(
        string organizationId,
        string environmentId,
        string reportNo,
        int sequenceNo,
        string serialNumber)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ReportNo = reportNo;
        SequenceNo = sequenceNo;
        SerialNumber = serialNumber;
    }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string ReportNo { get; private set; } = string.Empty;

    public int SequenceNo { get; private set; }

    public string SerialNumber { get; private set; } = string.Empty;

    public static IReadOnlyList<ProductionReportSerialNumber> CreateForReport(
        ProductionReport report,
        IReadOnlyList<string> serialNumbers)
    {
        ArgumentNullException.ThrowIfNull(serialNumbers);
        return CreateForNormalizedReport(report, Normalize(serialNumbers));
    }

    internal static IReadOnlyList<string> Normalize(IReadOnlyList<string> serialNumbers)
    {
        ArgumentNullException.ThrowIfNull(serialNumbers);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(serialNumbers.Count);
        foreach (var input in serialNumbers)
        {
            var serialNumber = DomainGuard.Required(input, nameof(serialNumbers));
            if (serialNumber.Length > SerialNumberMaxLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(serialNumbers),
                    $"Serial numbers cannot exceed {SerialNumberMaxLength} characters.");
            }

            if (!unique.Add(serialNumber))
            {
                throw new ArgumentException("Serial numbers must be unique after trimming.", nameof(serialNumbers));
            }

            normalized.Add(serialNumber);
        }

        return normalized;
    }

    internal static IReadOnlyList<ProductionReportSerialNumber> CreateForNormalizedReport(
        ProductionReport report,
        IReadOnlyList<string> serialNumbers)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.IsReversal)
        {
            throw new InvalidOperationException("Reversal production reports cannot own serial assignments.");
        }

        var result = new List<ProductionReportSerialNumber>(serialNumbers.Count);
        for (var index = 0; index < serialNumbers.Count; index++)
        {
            result.Add(new ProductionReportSerialNumber(
                report.OrganizationId,
                report.EnvironmentId,
                report.ReportNo,
                index + 1,
                serialNumbers[index]));
        }

        return result;
    }
}
