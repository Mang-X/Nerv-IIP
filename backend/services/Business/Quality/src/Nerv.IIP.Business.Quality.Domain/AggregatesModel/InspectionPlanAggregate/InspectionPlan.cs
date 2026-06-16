using Nerv.IIP.Business.Quality.Domain.DomainEvents;

namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;

public partial record InspectionPlanId : IGuidStronglyTypedId;

public partial record InspectionPlanCharacteristicId : IGuidStronglyTypedId;

public sealed class InspectionPlan : Entity<InspectionPlanId>, IAggregateRoot
{
    private static readonly HashSet<string> Categories =
    [
        "receiving",
        "operation",
        "final",
        "maintenance",
        "customer-return",
    ];

    private InspectionPlan()
    {
    }

    private InspectionPlan(
        string organizationId,
        string environmentId,
        string planCode,
        string category,
        string? skuCode,
        string? partnerId,
        string? workCenterId,
        string? deviceAssetId,
        string? documentType,
        int version,
        InspectionPlanId? supersedesPlanId)
    {
        Id = new InspectionPlanId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        PlanCode = Required(planCode);
        Category = Supported(category, Categories, nameof(category));
        SkuCode = Optional(skuCode);
        PartnerId = Optional(partnerId);
        WorkCenterId = Optional(workCenterId);
        DeviceAssetId = Optional(deviceAssetId);
        DocumentType = Optional(documentType);
        Version = version <= 0 ? throw new ArgumentOutOfRangeException(nameof(version), "Version must be positive.") : version;
        SupersedesPlanId = supersedesPlanId;
        Status = "draft";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PlanCode { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string? SkuCode { get; private set; }
    public string? PartnerId { get; private set; }
    public string? WorkCenterId { get; private set; }
    public string? DeviceAssetId { get; private set; }
    public string? DocumentType { get; private set; }
    public int Version { get; private set; }
    public InspectionPlanId? SupersedesPlanId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public List<InspectionPlanCharacteristic> Characteristics { get; private set; } = [];

    public static InspectionPlan Create(
        string organizationId,
        string environmentId,
        string planCode,
        string category,
        string? skuCode,
        string? partnerId,
        string? workCenterId,
        string? deviceAssetId,
        string? documentType)
    {
        return new InspectionPlan(
            organizationId,
            environmentId,
            planCode,
            category,
            skuCode,
            partnerId,
            workCenterId,
            deviceAssetId,
            documentType,
            1,
            null);
    }

    public void AddCharacteristic(
        string characteristicCode,
        string name,
        string method,
        string severity,
        bool required,
        string samplingRule)
    {
        AddCharacteristic(
            characteristicCode,
            name,
            method,
            severity,
            required,
            samplingRule,
            InspectionCharacteristicTypes.Attribute,
            null,
            null,
            null,
            null,
            null);
    }

    public void AddCharacteristic(
        string characteristicCode,
        string name,
        string method,
        string severity,
        bool required,
        string samplingRule,
        string characteristicType,
        decimal? nominalValue,
        decimal? lowerSpecLimit,
        decimal? upperSpecLimit,
        string? unitCode,
        InspectionSamplingPlan? samplingPlan)
    {
        EnsureDraft();
        var normalizedCode = Required(characteristicCode).ToLowerInvariant();
        if (Characteristics.Any(x => x.CharacteristicCode == normalizedCode))
        {
            throw new InvalidOperationException($"Inspection characteristic '{normalizedCode}' already exists.");
        }

        Characteristics.Add(new InspectionPlanCharacteristic(
            normalizedCode,
            name,
            method,
            severity,
            required,
            samplingRule,
            characteristicType,
            nominalValue,
            lowerSpecLimit,
            upperSpecLimit,
            unitCode,
            samplingPlan));
        Touch();
    }

    public void Activate()
    {
        EnsureDraft();
        if (Characteristics.Count == 0)
        {
            throw new InvalidOperationException("Inspection plan must have at least one characteristic before activation.");
        }

        Status = "active";
        ActivatedAtUtc = DateTime.UtcNow;
        Touch();
        this.AddDomainEvent(new InspectionPlanActivatedDomainEvent(this));
    }

    public InspectionPlan Supersede(string newPlanCode)
    {
        if (Status != "active")
        {
            throw new InvalidOperationException("Only active inspection plans can be superseded.");
        }

        Status = "superseded";
        Touch();
        var nextVersion = new InspectionPlan(
            OrganizationId,
            EnvironmentId,
            newPlanCode,
            Category,
            SkuCode,
            PartnerId,
            WorkCenterId,
            DeviceAssetId,
            DocumentType,
            Version + 1,
            Id);

        foreach (var characteristic in Characteristics)
        {
            nextVersion.Characteristics.Add(characteristic.Copy());
        }

        return nextVersion;
    }

    private void EnsureDraft()
    {
        if (Status != "draft")
        {
            throw new InvalidOperationException("Activated inspection plan execution characteristics are immutable.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Supported(string value, HashSet<string> supportedValues, string parameterName)
    {
        var normalized = Required(value).ToLowerInvariant();
        return supportedValues.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
    }
}

public sealed class InspectionPlanCharacteristic : Entity<InspectionPlanCharacteristicId>
{
    private InspectionPlanCharacteristic()
    {
    }

    internal InspectionPlanCharacteristic(
        string characteristicCode,
        string name,
        string method,
        string severity,
        bool required,
        string samplingRule,
        string characteristicType,
        decimal? nominalValue,
        decimal? lowerSpecLimit,
        decimal? upperSpecLimit,
        string? unitCode,
        InspectionSamplingPlan? samplingPlan)
    {
        CharacteristicCode = Required(characteristicCode).ToLowerInvariant();
        Name = Required(name);
        Method = Required(method);
        Severity = Required(severity).ToLowerInvariant();
        IsRequired = required;
        SamplingRule = Required(samplingRule);
        CharacteristicType = Supported(characteristicType, InspectionCharacteristicTypes.All, nameof(characteristicType));
        NominalValue = nominalValue;
        LowerSpecLimit = lowerSpecLimit;
        UpperSpecLimit = upperSpecLimit;
        UnitCode = Optional(unitCode);
        SamplingPlan = samplingPlan;
        ValidateSpecification();
    }

    public InspectionPlanId InspectionPlanId { get; private set; } = null!;
    public string CharacteristicCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Method { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }
    public string SamplingRule { get; private set; } = string.Empty;
    public string CharacteristicType { get; private set; } = string.Empty;
    public decimal? NominalValue { get; private set; }
    public decimal? LowerSpecLimit { get; private set; }
    public decimal? UpperSpecLimit { get; private set; }
    public string? UnitCode { get; private set; }
    public InspectionSamplingPlan? SamplingPlan { get; private set; }

    internal InspectionPlanCharacteristic Copy()
    {
        return new InspectionPlanCharacteristic(
            CharacteristicCode,
            Name,
            Method,
            Severity,
            IsRequired,
            SamplingRule,
            CharacteristicType,
            NominalValue,
            LowerSpecLimit,
            UpperSpecLimit,
            UnitCode,
            SamplingPlan);
    }

    private void ValidateSpecification()
    {
        if (LowerSpecLimit.HasValue && UpperSpecLimit.HasValue && LowerSpecLimit.Value > UpperSpecLimit.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(LowerSpecLimit), "Lower specification limit cannot exceed upper specification limit.");
        }

        if (CharacteristicType == InspectionCharacteristicTypes.Variable
            && !NominalValue.HasValue
            && !LowerSpecLimit.HasValue
            && !UpperSpecLimit.HasValue)
        {
            throw new ArgumentException("Variable characteristics require a nominal value or at least one specification limit.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Supported(string value, HashSet<string> supportedValues, string parameterName)
    {
        var normalized = Required(value).ToLowerInvariant();
        return supportedValues.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
    }
}

public sealed record InspectionSamplingPlan(
    string InspectionLevel,
    string Aql,
    int SampleSize,
    int AcceptanceNumber,
    int RejectionNumber)
{
    public static InspectionSamplingPlan Create(
        string inspectionLevel,
        string aql,
        int sampleSize,
        int acceptanceNumber,
        int rejectionNumber)
    {
        if (string.IsNullOrWhiteSpace(inspectionLevel))
        {
            throw new ArgumentException("Inspection level is required.", nameof(inspectionLevel));
        }

        if (string.IsNullOrWhiteSpace(aql))
        {
            throw new ArgumentException("AQL is required.", nameof(aql));
        }

        if (sampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "Sample size must be positive.");
        }

        if (acceptanceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptanceNumber), "Acceptance number cannot be negative.");
        }

        if (rejectionNumber <= acceptanceNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionNumber), "Rejection number must be greater than acceptance number.");
        }

        return new InspectionSamplingPlan(
            inspectionLevel.Trim().ToLowerInvariant(),
            aql.Trim(),
            sampleSize,
            acceptanceNumber,
            rejectionNumber);
    }
}

public static class InspectionCharacteristicTypes
{
    public const string Variable = "variable";
    public const string Attribute = "attribute";

    public static readonly HashSet<string> All = [Variable, Attribute];
}
