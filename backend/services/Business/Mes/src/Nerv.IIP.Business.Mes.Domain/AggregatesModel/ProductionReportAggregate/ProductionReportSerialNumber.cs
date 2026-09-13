namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportSerialNumberId : IGuidStronglyTypedId;

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
        string organizationId,
        string environmentId,
        string reportNo,
        IReadOnlyList<string> serialNumbers)
    {
        ArgumentNullException.ThrowIfNull(serialNumbers);
        var normalizedOrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        var normalizedEnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        var normalizedReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
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
                normalizedOrganizationId,
                normalizedEnvironmentId,
                normalizedReportNo,
                index + 1,
                serialNumber));
        }

        return result;
    }
}
