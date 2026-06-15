using Nerv.IIP.Business.BarcodeLabel.Domain;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

public partial record BarcodeRuleId : IGuidStronglyTypedId;

public sealed class BarcodeRule : Entity<BarcodeRuleId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedTypes = ["code128", "qr", "datamatrix", "gs1-128", "gs1-datamatrix"];

    private static readonly HashSet<string> SupportedStatuses = ["active", "inactive"];

    private BarcodeRule()
    {
    }

    private BarcodeRule(
        string organizationId,
        string environmentId,
        string ruleCode,
        string barcodeType,
        string prefix,
        int length,
        string checksumRule,
        IReadOnlyCollection<string> allowedSourceDocumentTypes,
        string status)
    {
        Id = new BarcodeRuleId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        RuleCode = BarcodeLabelText.Required(ruleCode, nameof(ruleCode));
        BarcodeType = BarcodeLabelText.Supported(barcodeType, SupportedTypes, nameof(BarcodeType));
        Prefix = BarcodeLabelText.Required(prefix, nameof(Prefix));
        Length = length <= 0 ? throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.") : length;
        ChecksumRule = BarcodeLabelText.Required(checksumRule, nameof(checksumRule)).ToLowerInvariant();
        AllowedSourceDocumentTypes = allowedSourceDocumentTypes
            .Select(x => BarcodeLabelText.Required(x, nameof(allowedSourceDocumentTypes)).ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (AllowedSourceDocumentTypes.Count == 0)
        {
            throw new ArgumentException("At least one source document type is required.", nameof(allowedSourceDocumentTypes));
        }

        Status = BarcodeLabelText.Supported(status, SupportedStatuses, nameof(status));
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RuleCode { get; private set; } = string.Empty;
    public string BarcodeType { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public int Length { get; private set; }
    public string ChecksumRule { get; private set; } = string.Empty;
    public List<string> AllowedSourceDocumentTypes { get; private set; } = [];
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static BarcodeRule Create(
        string organizationId,
        string environmentId,
        string ruleCode,
        string barcodeType,
        string prefix,
        int length,
        string checksumRule,
        IReadOnlyCollection<string> allowedSourceDocumentTypes,
        string status)
    {
        return new BarcodeRule(organizationId, environmentId, ruleCode, barcodeType, prefix, length, checksumRule, allowedSourceDocumentTypes, status);
    }

    public void Update(string barcodeType, string prefix, int length, string checksumRule, IReadOnlyCollection<string> allowedSourceDocumentTypes, string status)
    {
        BarcodeType = BarcodeLabelText.Supported(barcodeType, SupportedTypes, nameof(BarcodeType));
        Prefix = BarcodeLabelText.Required(prefix, nameof(Prefix));
        Length = length <= 0 ? throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.") : length;
        ChecksumRule = BarcodeLabelText.Required(checksumRule, nameof(checksumRule)).ToLowerInvariant();
        AllowedSourceDocumentTypes = allowedSourceDocumentTypes
            .Select(x => BarcodeLabelText.Required(x, nameof(allowedSourceDocumentTypes)).ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Status = BarcodeLabelText.Supported(status, SupportedStatuses, nameof(status));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string GenerateValue(string sourceDocumentType, string sourceDocumentId, int sequence)
    {
        if (Status != "active")
        {
            throw new InvalidOperationException("Only active barcode rules can generate label values.");
        }

        var sourceType = BarcodeLabelText.Required(sourceDocumentType, nameof(sourceDocumentType)).ToLowerInvariant();
        if (!AllowedSourceDocumentTypes.Contains(sourceType, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Source document type '{sourceDocumentType}' is not allowed by barcode rule '{RuleCode}'.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be positive.");
        }

        var value = $"{Prefix}{BarcodeLabelText.CodeToken(sourceDocumentId)}{sequence:D4}";
        if (value.Length > Length)
        {
            throw new InvalidOperationException($"Generated barcode exceeds configured length {Length}.");
        }

        return value;
    }

    public Gs1BarcodeValue GenerateGs1Value(string sourceDocumentType, string lotNo, string serialPrefix, int sequence)
    {
        if (!BarcodeType.StartsWith("gs1-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only GS1 barcode rules can generate GS1 values.");
        }

        if (ChecksumRule != "gs1-mod10")
        {
            throw new InvalidOperationException("GS1 barcode rules require gs1-mod10 checksum.");
        }

        var sourceType = BarcodeLabelText.Required(sourceDocumentType, nameof(sourceDocumentType)).ToLowerInvariant();
        if (!AllowedSourceDocumentTypes.Contains(sourceType, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Source document type '{sourceDocumentType}' is not allowed by barcode rule '{RuleCode}'.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be positive.");
        }

        var serialNumber = $"{BarcodeLabelText.Required(serialPrefix, nameof(serialPrefix))}{sequence:D4}";
        return Gs1BarcodeValue.Create(Prefix, lotNo, serialNumber);
    }
}
