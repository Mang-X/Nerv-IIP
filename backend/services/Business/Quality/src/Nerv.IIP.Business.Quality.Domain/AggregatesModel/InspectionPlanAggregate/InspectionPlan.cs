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

        if (!AqlZ14SamplingTable.MatchesThresholds(aql, sampleSize, acceptanceNumber, rejectionNumber))
        {
            throw new ArgumentException(
                $"AQL sampling thresholds must match ANSI/ASQ Z1.4 normal inspection for AQL {aql} and sample size {sampleSize}.",
                nameof(acceptanceNumber));
        }

        return new InspectionSamplingPlan(
            inspectionLevel.Trim().ToLowerInvariant(),
            aql.Trim(),
            sampleSize,
            acceptanceNumber,
            rejectionNumber);
    }

    public AqlResolvedSamplingPlan ResolveForLotSize(decimal lotQuantity, string severity)
    {
        return AqlZ14SamplingTable.Resolve(InspectionLevel, Aql, lotQuantity, severity);
    }
}

public sealed record AqlResolvedSamplingPlan(
    string CodeLetter,
    int SampleSize,
    int AcceptanceNumber,
    int RejectionNumber);

internal static class AqlZ14SamplingTable
{
    private static readonly IReadOnlyList<LotSizeCodeRow> LotSizeCodeRows =
    [
        new(8, "A", "A", "B", "A", "A", "A", "A"),
        new(15, "A", "B", "C", "A", "A", "A", "A"),
        new(25, "B", "C", "D", "A", "A", "B", "B"),
        new(50, "C", "D", "E", "A", "B", "B", "C"),
        new(90, "C", "E", "F", "B", "B", "C", "C"),
        new(150, "D", "F", "G", "B", "B", "C", "D"),
        new(280, "E", "G", "H", "B", "C", "D", "E"),
        new(500, "F", "H", "J", "B", "C", "D", "E"),
        new(1_200, "G", "J", "K", "C", "C", "E", "F"),
        new(3_200, "H", "K", "L", "C", "D", "E", "G"),
        new(10_000, "J", "L", "M", "C", "D", "F", "G"),
        new(35_000, "K", "M", "N", "C", "D", "F", "H"),
        new(150_000, "L", "N", "P", "D", "E", "G", "J"),
        new(500_000, "M", "P", "Q", "D", "E", "G", "J"),
        new(int.MaxValue, "N", "Q", "R", "D", "E", "H", "K"),
    ];

    private static readonly IReadOnlyDictionary<string, int> SampleSizeByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 2,
        ["B"] = 3,
        ["C"] = 5,
        ["D"] = 8,
        ["E"] = 13,
        ["F"] = 20,
        ["G"] = 32,
        ["H"] = 50,
        ["J"] = 80,
        ["K"] = 125,
        ["L"] = 200,
        ["M"] = 315,
        ["N"] = 500,
        ["P"] = 800,
        ["Q"] = 1_250,
        ["R"] = 2_000,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, SamplingScheme>> ThresholdsByAql =
        new Dictionary<string, IReadOnlyDictionary<int, SamplingScheme>>(StringComparer.OrdinalIgnoreCase)
        {
            ["0.065"] = SampleSizeByCode.Values.Distinct().ToDictionary(x => x, x => new SamplingScheme(x, AcceptanceNumber: 0, RejectionNumber: 1)),
            ["1.0"] = new Dictionary<int, SamplingScheme>
            {
                [2] = new(20, 0, 1),
                [3] = new(20, 0, 1),
                [5] = new(20, 0, 1),
                [8] = new(20, 0, 1),
                [13] = new(20, 0, 1),
                [20] = new(20, 0, 1),
                [32] = new(32, 1, 2),
                [50] = new(50, 1, 2),
                [80] = new(80, 2, 3),
                [125] = new(125, 3, 4),
                [200] = new(200, 5, 6),
                [315] = new(315, 7, 8),
                [500] = new(500, 10, 11),
                [800] = new(800, 14, 15),
                [1_250] = new(1_250, 21, 22),
                [2_000] = new(2_000, 21, 22),
            },
            ["2.5"] = new Dictionary<int, SamplingScheme>
            {
                [2] = new(8, 0, 1),
                [3] = new(8, 0, 1),
                [5] = new(8, 0, 1),
                [8] = new(8, 0, 1),
                [13] = new(13, 1, 2),
                [20] = new(20, 1, 2),
                [32] = new(32, 2, 3),
                [50] = new(50, 3, 4),
                [80] = new(80, 5, 6),
                [125] = new(125, 7, 8),
                [200] = new(200, 10, 11),
                [315] = new(315, 14, 15),
                [500] = new(500, 21, 22),
                [800] = new(800, 21, 22),
                [1_250] = new(1_250, 21, 22),
                [2_000] = new(2_000, 21, 22),
            },
        };

    public static AqlResolvedSamplingPlan Resolve(string inspectionLevel, string aql, decimal lotQuantity, string severity)
    {
        if (lotQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lotQuantity), "Lot quantity must be positive.");
        }

        var codeLetter = ResolveCodeLetter(inspectionLevel, lotQuantity);
        var sampleSize = SampleSizeByCode[codeLetter];
        var thresholds = ResolveThresholds(aql, sampleSize, severity);
        return new AqlResolvedSamplingPlan(codeLetter, thresholds.SampleSize, thresholds.AcceptanceNumber, thresholds.RejectionNumber);
    }

    public static bool MatchesThresholds(string aql, int sampleSize, int acceptanceNumber, int rejectionNumber)
    {
        return ThresholdsByAql.TryGetValue(aql.Trim(), out var thresholdsBySampleSize)
            && thresholdsBySampleSize.TryGetValue(sampleSize, out var thresholds)
            && thresholds.SampleSize == sampleSize
            && thresholds.AcceptanceNumber == acceptanceNumber
            && thresholds.RejectionNumber == rejectionNumber;
    }

    private static string ResolveCodeLetter(string inspectionLevel, decimal lotQuantity)
    {
        var row = LotSizeCodeRows.First(x => lotQuantity <= x.MaxLotQuantity);
        return NormalizeInspectionLevel(inspectionLevel) switch
        {
            "generali" => row.GeneralI,
            "generalii" => row.GeneralII,
            "generaliii" => row.GeneralIII,
            "s1" => row.SpecialS1,
            "s2" => row.SpecialS2,
            "s3" => row.SpecialS3,
            "s4" => row.SpecialS4,
            _ => throw new ArgumentException($"Unsupported inspection level '{inspectionLevel}'.", nameof(inspectionLevel)),
        };
    }

    private static SamplingScheme ResolveThresholds(string aql, int sampleSize, string severity)
    {
        if (string.Equals(severity.Trim(), "critical", StringComparison.OrdinalIgnoreCase))
        {
            return new SamplingScheme(sampleSize, AcceptanceNumber: 0, RejectionNumber: 1);
        }

        if (!ThresholdsByAql.TryGetValue(aql.Trim(), out var thresholdsBySampleSize)
            || !thresholdsBySampleSize.TryGetValue(sampleSize, out var thresholds))
        {
            throw new ArgumentException($"Unsupported AQL '{aql}' for sample size {sampleSize}.", nameof(aql));
        }

        return thresholds;
    }

    private sealed record SamplingScheme(
        int SampleSize,
        int AcceptanceNumber,
        int RejectionNumber);

    private static string NormalizeInspectionLevel(string inspectionLevel)
    {
        return inspectionLevel.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
    }

    private sealed record LotSizeCodeRow(
        int MaxLotQuantity,
        string GeneralI,
        string GeneralII,
        string GeneralIII,
        string SpecialS1,
        string SpecialS2,
        string SpecialS3,
        string SpecialS4);
}

public static class InspectionCharacteristicTypes
{
    public const string Variable = "variable";
    public const string Attribute = "attribute";

    public static readonly HashSet<string> All = [Variable, Attribute];
}
