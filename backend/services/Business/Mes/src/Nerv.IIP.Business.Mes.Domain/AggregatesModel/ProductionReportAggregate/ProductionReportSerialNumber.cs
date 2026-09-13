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
        IReadOnlyCollection<string>? serialNumbers)
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

        var inputs = serialNumbers ?? [];
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

        var normalized = new List<string>(inputs.Count);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new InvalidOperationException("Production serial numbers cannot be blank.");
            }

            var serialNumber = input.Trim();

            if (serialNumber.Length > ProductionReportSerialNumber.SerialNumberMaxLength)
            {
                throw new InvalidOperationException(
                    $"Production serial numbers cannot exceed {ProductionReportSerialNumber.SerialNumberMaxLength} characters.");
            }

            if (!unique.Add(serialNumber))
            {
                throw new InvalidOperationException("Production serial numbers must be unique after trimming.");
            }

            normalized.Add(serialNumber);
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
        ArgumentNullException.ThrowIfNull(report);
        if (report.IsReversal)
        {
            throw new InvalidOperationException("Reversal production reports cannot own serial assignments.");
        }

        return SerialNumbers
            .Select((serialNumber, index) => new ProductionReportSerialNumber(
                report.OrganizationId,
                report.EnvironmentId,
                report.ReportNo,
                index + 1,
                serialNumber))
            .ToArray();
    }
}

public sealed class ProductionReportSerialNumber : Entity<ProductionReportSerialNumberId>
{
    public const int SerialNumberMaxLength = 150;

    private ProductionReportSerialNumber()
    {
    }

    internal ProductionReportSerialNumber(
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
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(serialNumbers);
        if (report.IsReversal)
        {
            throw new InvalidOperationException("Reversal production reports cannot own serial assignments.");
        }

        var normalizedSerialNumbers = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ProductionReportSerialNumber>(serialNumbers.Count);

        for (var index = 0; index < serialNumbers.Count; index++)
        {
            var serialNumber = DomainGuard.Required(serialNumbers[index], nameof(serialNumbers));
            if (serialNumber.Length > SerialNumberMaxLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(serialNumbers),
                    $"Serial numbers cannot exceed {SerialNumberMaxLength} characters.");
            }

            if (!normalizedSerialNumbers.Add(serialNumber))
            {
                throw new ArgumentException("Serial numbers must be unique after trimming.", nameof(serialNumbers));
            }

            result.Add(new ProductionReportSerialNumber(
                report.OrganizationId,
                report.EnvironmentId,
                report.ReportNo,
                index + 1,
                serialNumber));
        }

        return result;
    }
}
