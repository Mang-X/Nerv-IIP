namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.SpcControlChartAggregate;

public partial record SpcControlChartId : IGuidStronglyTypedId;

public sealed class SpcControlChart : Entity<SpcControlChartId>, IAggregateRoot
{
    private SpcControlChart()
    {
    }

    private SpcControlChart(
        string organizationId,
        string environmentId,
        string skuCode,
        string characteristicCode,
        string workCenterId,
        int subgroupSize)
    {
        Id = new SpcControlChartId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        SkuCode = Required(skuCode);
        CharacteristicCode = Required(characteristicCode).ToLowerInvariant();
        WorkCenterId = Required(workCenterId);
        SubgroupSize = subgroupSize <= 1 ? throw new ArgumentOutOfRangeException(nameof(subgroupSize), "Subgroup size must be greater than 1.") : subgroupSize;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string CharacteristicCode { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public int SubgroupSize { get; private set; }
    public decimal CenterLine { get; private set; }
    public decimal AverageRange { get; private set; }
    public decimal XbarUpperControlLimit { get; private set; }
    public decimal XbarLowerControlLimit { get; private set; }
    public decimal RangeUpperControlLimit { get; private set; }
    public decimal RangeLowerControlLimit { get; private set; }
    public bool Locked { get; private set; }
    public DateTime? LimitsCalculatedAtUtc { get; private set; }
    public DateTime? LockedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SpcControlChart Create(
        string organizationId,
        string environmentId,
        string skuCode,
        string characteristicCode,
        string workCenterId,
        int subgroupSize)
    {
        return new SpcControlChart(organizationId, environmentId, skuCode, characteristicCode, workCenterId, subgroupSize);
    }

    public void LockLimits(
        decimal centerLine,
        decimal averageRange,
        decimal xbarUpperControlLimit,
        decimal xbarLowerControlLimit,
        decimal rangeUpperControlLimit,
        decimal rangeLowerControlLimit,
        DateTime calculatedAtUtc)
    {
        CenterLine = centerLine;
        AverageRange = averageRange;
        XbarUpperControlLimit = xbarUpperControlLimit;
        XbarLowerControlLimit = xbarLowerControlLimit;
        RangeUpperControlLimit = rangeUpperControlLimit;
        RangeLowerControlLimit = rangeLowerControlLimit;
        LimitsCalculatedAtUtc = calculatedAtUtc;
        Locked = true;
        LockedAtUtc = calculatedAtUtc;
        UpdatedAtUtc = calculatedAtUtc;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
