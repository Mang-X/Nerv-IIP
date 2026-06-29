using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;

namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

public partial record InspectionRecordId : IGuidStronglyTypedId;

public partial record InspectionResultLineId : IGuidStronglyTypedId;

public sealed class InspectionRecord : Entity<InspectionRecordId>, IAggregateRoot
{
    private static readonly HashSet<string> SourceTypes =
    [
        "receiving",
        "operation",
        "final",
        "maintenance",
        "customer-return",
    ];

    private static readonly HashSet<string> SourceServices =
    [
        "inventory",
        "wms",
        "mes",
        "erp",
        "maintenance",
        "purchase-receipt",
        "mes-operation",
        "customer-return",
    ];

    private InspectionRecord()
    {
    }

    private InspectionRecord(
        string organizationId,
        string environmentId,
        InspectionPlanId? inspectionPlanId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        string? batchNo,
        string? serialNo,
        StockReleaseDimension? stockRelease,
        IReadOnlyCollection<InspectionResultLineInput> resultLines,
        string? dispositionReason,
        IReadOnlyCollection<string> dispositionAttachmentFileIds,
        IReadOnlyCollection<InspectionUomConversion>? uomConversions = null)
    {
        Id = new InspectionRecordId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        InspectionPlanId = inspectionPlanId;
        SourceType = Supported(sourceType, SourceTypes, nameof(sourceType));
        SourceService = Supported(sourceService, SourceServices, nameof(sourceService));
        SourceDocumentId = Required(sourceDocumentId);
        SkuCode = Required(skuCode);
        InspectedQuantity = Positive(inspectedQuantity, nameof(inspectedQuantity));
        BatchNo = Optional(batchNo);
        SerialNo = Optional(serialNo);
        ApplyStockRelease(stockRelease);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;

        if (resultLines.Count == 0)
        {
            throw new ArgumentException("Inspection record must contain result lines.", nameof(resultLines));
        }

        ResultLines.AddRange(resultLines.Select(x => new InspectionResultLine(
            x.CharacteristicCode,
            x.ObservedValue,
            x.MeasuredValue,
            x.UnitCode,
            x.Result,
            x.DefectReason,
            x.DefectQuantity,
            x.AttachmentFileIds)));
        Result = CalculateResult(ResultLines);

        if (Result != InspectionRecordResults.Passed)
        {
            DispositionReason = Required(dispositionReason ?? string.Empty);
            DispositionAttachmentFileIds.AddRange(dispositionAttachmentFileIds.Select(Required).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        AddResultDomainEvent();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public InspectionPlanId? InspectionPlanId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public string SourceService { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public decimal InspectedQuantity { get; private set; }
    public string? BatchNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string? UomCode { get; private set; }
    public string? SiteCode { get; private set; }
    public string? LocationCode { get; private set; }
    public string? SourceQualityStatus { get; private set; }
    public string? OwnerType { get; private set; }
    public string? OwnerId { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public string? DispositionReason { get; private set; }
    public List<string> DispositionAttachmentFileIds { get; private set; } = [];
    public List<InspectionResultLine> ResultLines { get; private set; } = [];
    public string? NonconformanceReportId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static InspectionRecord Create(
        string organizationId,
        string environmentId,
        InspectionPlanId? inspectionPlanId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        string? batchNo,
        string? serialNo,
        IReadOnlyCollection<InspectionResultLineInput> resultLines,
        string? dispositionReason,
        IReadOnlyCollection<string> dispositionAttachmentFileIds,
        StockReleaseDimension? stockRelease = null)
    {
        return new InspectionRecord(
            organizationId,
            environmentId,
            inspectionPlanId,
            sourceType,
            sourceService,
            sourceDocumentId,
            skuCode,
            inspectedQuantity,
            batchNo,
            serialNo,
            stockRelease,
            resultLines,
            dispositionReason,
            dispositionAttachmentFileIds);
    }

    public static InspectionRecord CreateFromPlan(
        InspectionPlan inspectionPlan,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        string? batchNo,
        string? serialNo,
        StockReleaseDimension? stockRelease,
        IReadOnlyCollection<InspectionResultLineInput> resultLines,
        string? dispositionReason,
        IReadOnlyCollection<string> dispositionAttachmentFileIds,
        IReadOnlyCollection<InspectionUomConversion>? uomConversions = null)
    {
        ArgumentNullException.ThrowIfNull(inspectionPlan);
        if (inspectionPlan.Status != "active")
        {
            throw new InvalidOperationException("Planned inspection records require an active inspection plan.");
        }

        var normalizedSourceType = Supported(sourceType, SourceTypes, nameof(sourceType));
        if (!string.Equals(inspectionPlan.Category, normalizedSourceType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Inspection plan category '{inspectionPlan.Category}' does not match record source type '{normalizedSourceType}'.");
        }

        if (!string.IsNullOrWhiteSpace(inspectionPlan.SkuCode)
            && !string.Equals(inspectionPlan.SkuCode, skuCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Inspection plan SKU '{inspectionPlan.SkuCode}' does not match inspected SKU '{skuCode}'.");
        }

        var plannedLines = CalculatePlannedLines(inspectionPlan, resultLines, inspectedQuantity, uomConversions ?? []);
        return new InspectionRecord(
            inspectionPlan.OrganizationId,
            inspectionPlan.EnvironmentId,
            inspectionPlan.Id,
            normalizedSourceType,
            sourceService,
            sourceDocumentId,
            skuCode,
            inspectedQuantity,
            batchNo,
            serialNo,
            stockRelease,
            plannedLines,
            dispositionReason,
            dispositionAttachmentFileIds);
    }

    public void LinkNonconformanceReport(string ncrId)
    {
        if (Result == InspectionRecordResults.Passed)
        {
            throw new InvalidOperationException("Passed inspections cannot be linked to an NCR.");
        }

        if (!string.IsNullOrWhiteSpace(NonconformanceReportId))
        {
            throw new InvalidOperationException("Inspection record is already linked to an NCR.");
        }

        NonconformanceReportId = Required(ncrId);
        Touch();
    }

    public decimal FailedQuantity()
    {
        return ResultLines
            .Where(x => x.Result is InspectionLineResults.Failed or InspectionLineResults.ConditionalRelease)
            .Sum(x => x.DefectQuantity ?? 0m);
    }

    private void AddResultDomainEvent()
    {
        if (Result == InspectionRecordResults.Passed)
        {
            this.AddDomainEvent(new InspectionPassedDomainEvent(this));
            return;
        }

        if (Result == InspectionRecordResults.ConditionalRelease)
        {
            this.AddDomainEvent(new InspectionConditionalReleasedDomainEvent(this));
            return;
        }

        this.AddDomainEvent(new InspectionRejectedDomainEvent(this));
    }

    private void ApplyStockRelease(StockReleaseDimension? stockRelease)
    {
        if (stockRelease is null)
        {
            return;
        }

        UomCode = stockRelease.UomCode;
        SiteCode = stockRelease.SiteCode;
        LocationCode = stockRelease.LocationCode;
        SourceQualityStatus = stockRelease.SourceQualityStatus;
        OwnerType = stockRelease.OwnerType;
        OwnerId = stockRelease.OwnerId;
    }

    private static IReadOnlyCollection<InspectionResultLineInput> CalculatePlannedLines(
        InspectionPlan inspectionPlan,
        IReadOnlyCollection<InspectionResultLineInput> resultLines,
        decimal inspectedQuantity,
        IReadOnlyCollection<InspectionUomConversion> uomConversions)
    {
        if (resultLines.Count == 0)
        {
            throw new ArgumentException("Inspection record must contain result lines.", nameof(resultLines));
        }

        var inputByCode = resultLines
            .GroupBy(x => x.CharacteristicCode.Trim().ToLowerInvariant())
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var missingRequired = inspectionPlan.Characteristics
            .Where(x => x.IsRequired && !inputByCode.ContainsKey(x.CharacteristicCode))
            .Select(x => x.CharacteristicCode)
            .ToArray();
        if (missingRequired.Length > 0)
        {
            throw new InvalidOperationException($"Inspection record is missing required characteristic(s): {string.Join(", ", missingRequired)}.");
        }

        var plannedLines = inspectionPlan.Characteristics
            .Where(x => inputByCode.ContainsKey(x.CharacteristicCode))
            .Select(x => new PlannedLineEvaluation(
                x,
                CalculatePlannedLine(x, inputByCode[x.CharacteristicCode], inspectedQuantity, uomConversions)))
            .ToArray();
        return ApplyAqlSeverityDecisions(plannedLines, inspectedQuantity);
    }

    private static InspectionResultLineInput CalculatePlannedLine(
        InspectionPlanCharacteristic characteristic,
        InspectionResultLineInput input,
        decimal inspectedQuantity,
        IReadOnlyCollection<InspectionUomConversion> uomConversions)
    {
        if (characteristic.SamplingPlan is not null)
        {
            var resolvedSamplingPlan = characteristic.SamplingPlan.ResolveForLotSize(inspectedQuantity, characteristic.Severity);
            if (inspectedQuantity < resolvedSamplingPlan.SampleSize)
            {
                throw new InvalidOperationException($"Inspection characteristic '{characteristic.CharacteristicCode}' requires sample size {resolvedSamplingPlan.SampleSize}.");
            }
        }

        return characteristic.CharacteristicType switch
        {
            InspectionCharacteristicTypes.Variable => CalculateVariableLine(characteristic, input, inspectedQuantity, uomConversions),
            InspectionCharacteristicTypes.Attribute => CalculateAttributeLine(characteristic, input),
            _ => throw new InvalidOperationException($"Unsupported characteristic type '{characteristic.CharacteristicType}'."),
        };
    }

    private static InspectionResultLineInput CalculateVariableLine(
        InspectionPlanCharacteristic characteristic,
        InspectionResultLineInput input,
        decimal inspectedQuantity,
        IReadOnlyCollection<InspectionUomConversion> uomConversions)
    {
        if (!input.MeasuredValue.HasValue)
        {
            throw new InvalidOperationException($"Variable characteristic '{characteristic.CharacteristicCode}' requires measured value.");
        }

        var measuredValueInPlanUnit = input.MeasuredValue.Value;
        var resultUnitCode = input.UnitCode ?? characteristic.UnitCode;
        if (!string.IsNullOrWhiteSpace(characteristic.UnitCode)
            && !string.IsNullOrWhiteSpace(input.UnitCode)
            && !string.Equals(characteristic.UnitCode, input.UnitCode, StringComparison.OrdinalIgnoreCase))
        {
            measuredValueInPlanUnit = ConvertMeasuredValue(characteristic, input, uomConversions);
            resultUnitCode = characteristic.UnitCode;
        }

        var failed = characteristic.LowerSpecLimit.HasValue && measuredValueInPlanUnit < characteristic.LowerSpecLimit.Value
            || characteristic.UpperSpecLimit.HasValue && measuredValueInPlanUnit > characteristic.UpperSpecLimit.Value;
        var conditionalRelease = failed && input.Result == InspectionLineResults.ConditionalRelease;
        return input with
        {
            ObservedValue = measuredValueInPlanUnit.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
            UnitCode = resultUnitCode,
            Result = failed
                ? conditionalRelease ? InspectionLineResults.ConditionalRelease : InspectionLineResults.Failed
                : InspectionLineResults.Passed,
            DefectReason = failed ? input.DefectReason ?? "out-of-specification" : input.DefectReason,
            DefectQuantity = failed ? input.DefectQuantity ?? inspectedQuantity : input.DefectQuantity,
        };
    }

    private static decimal ConvertMeasuredValue(
        InspectionPlanCharacteristic characteristic,
        InspectionResultLineInput input,
        IReadOnlyCollection<InspectionUomConversion> uomConversions)
    {
        var conversion = uomConversions.SingleOrDefault(x =>
            string.Equals(x.FromUomCode, input.UnitCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ToUomCode, characteristic.UnitCode, StringComparison.OrdinalIgnoreCase));
        if (conversion is null)
        {
            throw new InvalidOperationException($"Characteristic '{characteristic.CharacteristicCode}' unit '{input.UnitCode}' does not match plan unit '{characteristic.UnitCode}' and no UOM conversion was supplied.");
        }

        return conversion.Convert(input.MeasuredValue!.Value);
    }

    private static InspectionResultLineInput CalculateAttributeLine(
        InspectionPlanCharacteristic characteristic,
        InspectionResultLineInput input)
    {
        if (characteristic.SamplingPlan is null)
        {
            if (input.Result == InspectionLineResults.Passed
                && (input.DefectQuantity.GetValueOrDefault() > 0m || !string.IsNullOrWhiteSpace(input.DefectReason)))
            {
                return input with
                {
                    Result = InspectionLineResults.Failed,
                    DefectReason = input.DefectReason ?? "attribute-defect-observed",
                };
            }

            return input;
        }

        var defectQuantity = input.DefectQuantity ?? 0m;
        return input with
        {
            Result = InspectionLineResults.Passed,
            DefectQuantity = defectQuantity,
        };
    }

    private static IReadOnlyCollection<InspectionResultLineInput> ApplyAqlSeverityDecisions(
        IReadOnlyCollection<PlannedLineEvaluation> plannedLines,
        decimal inspectedQuantity)
    {
        var lineByCharacteristicCode = plannedLines.ToDictionary(x => x.Characteristic.CharacteristicCode, x => x.Line, StringComparer.OrdinalIgnoreCase);
        var aqlGroups = plannedLines
            .Where(x => x.Characteristic.CharacteristicType == InspectionCharacteristicTypes.Attribute
                && x.Characteristic.SamplingPlan is not null)
            .GroupBy(x => x.Characteristic.Severity, StringComparer.OrdinalIgnoreCase);

        foreach (var group in aqlGroups)
        {
            var groupLines = group.ToArray();
            var firstSamplingPlan = groupLines[0].Characteristic.SamplingPlan!;
            if (groupLines.Any(x => !HasSameSamplingPlan(firstSamplingPlan, x.Characteristic.SamplingPlan!)))
            {
                throw new InvalidOperationException($"AQL sampling plan must be consistent for severity '{group.Key}'.");
            }

            var resolved = firstSamplingPlan.ResolveForLotSize(inspectedQuantity, group.Key);
            var defectQuantity = groupLines.Sum(x => x.Line.DefectQuantity ?? 0m);
            var result = defectQuantity <= resolved.AcceptanceNumber
                ? InspectionLineResults.Passed
                : defectQuantity >= resolved.RejectionNumber
                    ? InspectionLineResults.Failed
                    : InspectionLineResults.ConditionalRelease;
            var reason = result == InspectionLineResults.Failed
                ? string.Equals(group.Key, "critical", StringComparison.OrdinalIgnoreCase)
                    ? "critical-defect-observed"
                    : "aql-rejection-number-reached"
                : "aql-conditional-release";

            foreach (var evaluation in groupLines)
            {
                var lineDefectQuantity = evaluation.Line.DefectQuantity ?? 0m;
                if (result == InspectionLineResults.Passed || lineDefectQuantity <= 0m)
                {
                    continue;
                }

                lineByCharacteristicCode[evaluation.Characteristic.CharacteristicCode] = evaluation.Line with
                {
                    Result = result,
                    DefectReason = evaluation.Line.DefectReason ?? reason,
                };
            }
        }

        return plannedLines.Select(x => lineByCharacteristicCode[x.Characteristic.CharacteristicCode]).ToArray();
    }

    private static bool HasSameSamplingPlan(InspectionSamplingPlan first, InspectionSamplingPlan second)
    {
        return string.Equals(first.InspectionLevel, second.InspectionLevel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.Aql, second.Aql, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PlannedLineEvaluation(
        InspectionPlanCharacteristic Characteristic,
        InspectionResultLineInput Line);

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string CalculateResult(IReadOnlyCollection<InspectionResultLine> resultLines)
    {
        return resultLines.Any(x => x.Result == InspectionLineResults.Failed)
            ? InspectionRecordResults.Rejected
            : resultLines.Any(x => x.Result == InspectionLineResults.ConditionalRelease)
                ? InspectionRecordResults.ConditionalRelease
                : InspectionRecordResults.Passed;
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, "Value must be positive.") : value;
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

public sealed class InspectionResultLine : Entity<InspectionResultLineId>
{
    private InspectionResultLine()
    {
    }

    internal InspectionResultLine(
        string characteristicCode,
        string observedValue,
        decimal? measuredValue,
        string? unitCode,
        string result,
        string? defectReason,
        decimal? defectQuantity,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        CharacteristicCode = Required(characteristicCode).ToLowerInvariant();
        ObservedValue = Required(observedValue);
        MeasuredValue = measuredValue;
        UnitCode = Optional(unitCode);
        Result = Supported(result, InspectionLineResults.All, nameof(result));
        DefectReason = Optional(defectReason);
        DefectQuantity = defectQuantity;
        AttachmentFileIds.AddRange(attachmentFileIds.Select(Required).Distinct(StringComparer.OrdinalIgnoreCase));

        if (Result != InspectionLineResults.Passed && string.IsNullOrWhiteSpace(DefectReason))
        {
            throw new ArgumentException("Failed or conditional-release lines must preserve a defect or disposition reason.", nameof(defectReason));
        }

        if (Result == InspectionLineResults.ConditionalRelease && defectQuantity is null or <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defectQuantity), "Conditional-release lines must provide a positive defect quantity.");
        }
    }

    public InspectionRecordId InspectionRecordId { get; private set; } = null!;
    public string CharacteristicCode { get; private set; } = string.Empty;
    public string ObservedValue { get; private set; } = string.Empty;
    public decimal? MeasuredValue { get; private set; }
    public string? UnitCode { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public string? DefectReason { get; private set; }
    public decimal? DefectQuantity { get; private set; }
    public List<string> AttachmentFileIds { get; private set; } = [];

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

public sealed record InspectionResultLineInput(
    string CharacteristicCode,
    string ObservedValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string> AttachmentFileIds,
    decimal? MeasuredValue = null)
{
    public static InspectionResultLineInput Pass(string characteristicCode, string observedValue, string? unitCode, IReadOnlyCollection<string> attachmentFileIds)
    {
        return new InspectionResultLineInput(characteristicCode, observedValue, unitCode, InspectionLineResults.Passed, null, null, attachmentFileIds);
    }

    public static InspectionResultLineInput Measure(string characteristicCode, decimal measuredValue, string? unitCode, IReadOnlyCollection<string> attachmentFileIds)
    {
        return new InspectionResultLineInput(
            characteristicCode,
            measuredValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            unitCode,
            InspectionLineResults.Passed,
            null,
            null,
            attachmentFileIds,
            measuredValue);
    }

    public static InspectionResultLineInput Attribute(
        string characteristicCode,
        string observedText,
        string? defectReason,
        decimal? defectQuantity,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        return new InspectionResultLineInput(
            characteristicCode,
            observedText,
            null,
            InspectionLineResults.Passed,
            defectReason,
            defectQuantity,
            attachmentFileIds);
    }

    public static InspectionResultLineInput Fail(
        string characteristicCode,
        string observedValue,
        string defectReason,
        decimal? defectQuantity,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        return new InspectionResultLineInput(characteristicCode, observedValue, null, InspectionLineResults.Failed, defectReason, defectQuantity, attachmentFileIds);
    }

    public static InspectionResultLineInput ConditionalRelease(
        string characteristicCode,
        string observedValue,
        string defectReason,
        decimal defectQuantity,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        return new InspectionResultLineInput(characteristicCode, observedValue, null, InspectionLineResults.ConditionalRelease, defectReason, defectQuantity, attachmentFileIds);
    }
}

public sealed record InspectionUomConversion(
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode)
{
    public static InspectionUomConversion Create(
        string fromUomCode,
        string toUomCode,
        decimal factor,
        decimal offset,
        int precision,
        string roundingMode)
    {
        if (factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "Conversion factor must be positive.");
        }

        return new InspectionUomConversion(
            Required(fromUomCode),
            Required(toUomCode),
            factor,
            offset,
            Math.Clamp(precision, 0, 12),
            Required(roundingMode));
    }

    public decimal Convert(decimal value)
    {
        return Normalize(RoundingMode) switch
        {
            "BANKERS" or "TO-EVEN" or "TOEVEN" => Math.Round(value * Factor + Offset, Precision, MidpointRounding.ToEven),
            "CEILING" or "UP" => RoundToward(value * Factor + Offset, Precision, ceiling: true),
            "FLOOR" or "DOWN" => RoundToward(value * Factor + Offset, Precision, ceiling: false),
            _ => Math.Round(value * Factor + Offset, Precision, MidpointRounding.AwayFromZero),
        };
    }

    private static decimal RoundToward(decimal value, int digits, bool ceiling)
    {
        var scale = (decimal)Math.Pow(10, digits);
        return (ceiling ? Math.Ceiling(value * scale) : Math.Floor(value * scale)) / scale;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}

public static class InspectionRecordResults
{
    public const string Passed = "passed";
    public const string Rejected = "rejected";
    public const string ConditionalRelease = "conditional-release";
}

public static class InspectionLineResults
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string ConditionalRelease = "conditional-release";

    public static readonly HashSet<string> All = [Passed, Failed, ConditionalRelease];
}

public sealed record StockReleaseDimension(
    string UomCode,
    string SiteCode,
    string LocationCode,
    string SourceQualityStatus,
    string OwnerType,
    string? OwnerId)
{
    public static StockReleaseDimension Create(
        string uomCode,
        string siteCode,
        string locationCode,
        string sourceQualityStatus,
        string ownerType,
        string? ownerId)
    {
        return new StockReleaseDimension(
            Required(uomCode),
            Required(siteCode),
            Required(locationCode),
            Required(sourceQualityStatus).ToLowerInvariant(),
            Required(ownerType).ToLowerInvariant(),
            Optional(ownerId));
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
