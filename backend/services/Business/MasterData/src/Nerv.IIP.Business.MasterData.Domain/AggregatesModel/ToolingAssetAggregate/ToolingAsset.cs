namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

public partial record ToolingAssetId : IGuidStronglyTypedId;
public partial record ToolingApplicabilityId : IGuidStronglyTypedId;
public partial record ChangeoverMatrixEntryId : IGuidStronglyTypedId;
public partial record ChangeoverRequiredToolingId : IGuidStronglyTypedId;

public enum ChangeoverSourceType { Sku = 0, ProductFamily = 1 }

public enum ToolingAssetStatus
{
    Available = 0,
    Maintenance = 1,
    Retired = 2
}

public class ToolingAsset : Entity<ToolingAssetId>, IAggregateRoot
{
    private readonly List<ToolingApplicability> applicability = [];

    protected ToolingAsset() { }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string ToolingType { get; private set; } = string.Empty;
    public ToolingAssetStatus Status { get; private set; }
    public long? MaintenanceLifeCount { get; private set; }
    public long UsageCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ToolingApplicability> Applicability => applicability.AsReadOnly();
    public bool IsSchedulable => Status == ToolingAssetStatus.Available && (!MaintenanceLifeCount.HasValue || UsageCount < MaintenanceLifeCount.Value);

    public static ToolingAsset Register(
        string organizationId,
        string environmentId,
        string code,
        string name,
        string toolingType,
        IEnumerable<string> workCenterCodes,
        IEnumerable<string> skuCodes,
        long? maintenanceLifeCount)
    {
        if (maintenanceLifeCount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maintenanceLifeCount));

        var workCenters = Normalize(workCenterCodes);
        var skus = Normalize(skuCodes);
        if (workCenters.Length == 0 || skus.Length == 0)
            throw new ArgumentException("Tooling requires at least one work center and SKU applicability.");

        var asset = new ToolingAsset
        {
            OrganizationId = Required(organizationId),
            EnvironmentId = Required(environmentId),
            Code = Required(code),
            Name = Required(name),
            ToolingType = Required(toolingType),
            Status = ToolingAssetStatus.Available,
            MaintenanceLifeCount = maintenanceLifeCount,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        asset.applicability.AddRange(from workCenter in workCenters from sku in skus select ToolingApplicability.Create(workCenter, sku));
        return asset;
    }

    public bool IsApplicable(string workCenterCode, string skuCode) => IsSchedulable && applicability.Any(x =>
        string.Equals(x.WorkCenterCode, workCenterCode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.SkuCode, skuCode, StringComparison.OrdinalIgnoreCase));

    public void RecordUsage(long count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (Status == ToolingAssetStatus.Retired) throw new InvalidOperationException("Retired tooling cannot record usage.");
        UsageCount = checked(UsageCount + count);
        if (MaintenanceLifeCount.HasValue && UsageCount >= MaintenanceLifeCount.Value)
            Status = ToolingAssetStatus.Maintenance;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ChangeStatus(ToolingAssetStatus status, string reason)
    {
        _ = Required(reason);
        if (Status == ToolingAssetStatus.Retired && status != ToolingAssetStatus.Retired)
            throw new InvalidOperationException("Retired tooling cannot return to service.");
        Status = status;
        if (status == ToolingAssetStatus.Available && MaintenanceLifeCount.HasValue && UsageCount >= MaintenanceLifeCount.Value)
            UsageCount = 0;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string[] Normalize(IEnumerable<string> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.") : value.Trim();
}

public sealed class ToolingApplicability : Entity<ToolingApplicabilityId>
{
    private ToolingApplicability() { }
    public string WorkCenterCode { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    internal static ToolingApplicability Create(string workCenterCode, string skuCode) => new()
    {
        Id = new ToolingApplicabilityId(Guid.CreateVersion7()),
        WorkCenterCode = workCenterCode,
        SkuCode = skuCode
    };
}

public class ChangeoverMatrixEntry : Entity<ChangeoverMatrixEntryId>, IAggregateRoot
{
    private readonly List<ChangeoverRequiredTooling> requiredTooling = [];
    protected ChangeoverMatrixEntry() { }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkCenterCode { get; private set; } = string.Empty;
    public ChangeoverSourceType SourceType { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string? FromSkuCode => SourceType == ChangeoverSourceType.Sku ? SourceCode : null;
    public string? FromProductFamilyCode => SourceType == ChangeoverSourceType.ProductFamily ? SourceCode : null;
    public string ToSkuCode { get; private set; } = string.Empty;
    public int SetupMinutes { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ChangeoverRequiredTooling> RequiredTooling => requiredTooling.AsReadOnly();
    public int Specificity => string.IsNullOrWhiteSpace(FromSkuCode) ? 1 : 2;

    public static ChangeoverMatrixEntry Create(string organizationId, string environmentId, string workCenterCode, string? fromSkuCode, string? fromProductFamilyCode, string toSkuCode, int setupMinutes, IEnumerable<string> requiredToolingCodes)
    {
        if (setupMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(setupMinutes));
        var tooling = requiredToolingCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tooling.Length == 0) throw new ArgumentException("Changeover requires at least one tooling asset.", nameof(requiredToolingCodes));
        if (string.IsNullOrWhiteSpace(fromSkuCode) == string.IsNullOrWhiteSpace(fromProductFamilyCode))
            throw new ArgumentException("Specify exactly one from SKU or product family.");
        var now = DateTime.UtcNow;
        var entry = new ChangeoverMatrixEntry
        {
            OrganizationId = Required(organizationId), EnvironmentId = Required(environmentId), WorkCenterCode = Required(workCenterCode),
            SourceType = string.IsNullOrWhiteSpace(fromSkuCode) ? ChangeoverSourceType.ProductFamily : ChangeoverSourceType.Sku,
            SourceCode = Required(fromSkuCode ?? fromProductFamilyCode!), ToSkuCode = Required(toSkuCode),
            SetupMinutes = setupMinutes, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        entry.requiredTooling.AddRange(tooling.Select(ChangeoverRequiredTooling.Create));
        return entry;
    }

    public void Update(int setupMinutes, IEnumerable<string> requiredToolingCodes, bool active)
    {
        if (setupMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(setupMinutes));
        var tooling = requiredToolingCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tooling.Length == 0) throw new ArgumentException("Changeover requires at least one tooling asset.", nameof(requiredToolingCodes));
        SetupMinutes = setupMinutes;
        Active = active;
        requiredTooling.Clear();
        requiredTooling.AddRange(tooling.Select(ChangeoverRequiredTooling.Create));
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public bool Matches(string fromSkuCode, string? fromProductFamilyCode, string toSkuCode, string workCenterCode) => Active
        && string.Equals(WorkCenterCode, workCenterCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ToSkuCode, toSkuCode, StringComparison.OrdinalIgnoreCase)
        && ((!string.IsNullOrWhiteSpace(FromSkuCode) && string.Equals(FromSkuCode, fromSkuCode, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(FromProductFamilyCode) && string.Equals(FromProductFamilyCode, fromProductFamilyCode, StringComparison.OrdinalIgnoreCase)));

    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.") : value.Trim();
}

public sealed class ChangeoverRequiredTooling : Entity<ChangeoverRequiredToolingId>
{
    private ChangeoverRequiredTooling() { }
    public string ToolingCode { get; private set; } = string.Empty;
    internal static ChangeoverRequiredTooling Create(string toolingCode) => new() { Id = new ChangeoverRequiredToolingId(Guid.CreateVersion7()), ToolingCode = toolingCode };
}
