using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

public partial record ScanRecordId : IGuidStronglyTypedId;

public sealed class ScanRecord : Entity<ScanRecordId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedResults = ["accepted", "rejected"];
    private static readonly HashSet<string> SupportedAcceptedWorkflows =
    [
        "wms.receiving",
        "inventory.receipt",
        "inventory.issue",
        "inventory.adjustment",
        "inventory.count",
        "production.report",
        "quality.inspection",
    ];

    private ScanRecord()
    {
    }

    private ScanRecord(
        string organizationId,
        string environmentId,
        string deviceCode,
        string scannedValue,
        string sourceWorkflow,
        string sourceDocumentId,
        string idempotencyKey,
        string result,
        string? rejectionReason,
        string? skuCode,
        string? uomCode,
        string? siteCode,
        string? locationCode,
        string? qualityStatus,
        string? ownerType,
        string? ownerId,
        decimal? quantity)
    {
        Id = new ScanRecordId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        DeviceCode = BarcodeLabelText.Required(deviceCode, nameof(deviceCode));
        ScannedValue = BarcodeLabelText.Required(scannedValue, nameof(scannedValue));
        SourceWorkflow = BarcodeLabelText.Required(sourceWorkflow, nameof(sourceWorkflow)).ToLowerInvariant();
        SourceDocumentId = BarcodeLabelText.Required(sourceDocumentId, nameof(sourceDocumentId));
        IdempotencyKey = BarcodeLabelText.Required(idempotencyKey, nameof(idempotencyKey));
        Result = BarcodeLabelText.Supported(result, SupportedResults, nameof(result));
        RejectionReason = BarcodeLabelText.Optional(rejectionReason);
        SkuCode = BarcodeLabelText.Optional(skuCode);
        UomCode = BarcodeLabelText.Optional(uomCode);
        SiteCode = BarcodeLabelText.Optional(siteCode);
        LocationCode = BarcodeLabelText.Optional(locationCode);
        QualityStatus = BarcodeLabelText.Optional(qualityStatus);
        OwnerType = BarcodeLabelText.Optional(ownerType);
        OwnerId = BarcodeLabelText.Optional(ownerId);
        Quantity = quantity;
        ScannedAtUtc = DateTimeOffset.UtcNow;

        if (Result == "rejected")
        {
            this.AddDomainEvent(new ScanRejectedDomainEvent(this));
        }
        else
        {
            if (!IsSupportedAcceptedWorkflow(SourceWorkflow))
            {
                throw new ArgumentException($"Unsupported SourceWorkflow '{sourceWorkflow}'.", nameof(sourceWorkflow));
            }

            ParseGs1ValueIfPresent();
            ConfigureBusinessAction();
            if (!string.IsNullOrWhiteSpace(SerialNumber))
            {
                EpcisEvents.Add(EpcisEvent.ObjectEvent(OrganizationId, EnvironmentId, this));
            }

            if (SourceWorkflow == "wms.receiving" && !string.IsNullOrWhiteSpace(Sscc) && !string.IsNullOrWhiteSpace(SerialNumber))
            {
                EpcisEvents.Add(EpcisEvent.Aggregation(OrganizationId, EnvironmentId, this, SourceWorkflow, SourceDocumentId));
            }

            this.AddDomainEvent(new LabelScannedDomainEvent(this));
            if (BusinessAction == "inventory-movement-requested")
            {
                this.AddDomainEvent(new InventoryMovementRequestedFromScanDomainEvent(this));
            }
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceCode { get; private set; } = string.Empty;
    public string ScannedValue { get; private set; } = string.Empty;
    public string SourceWorkflow { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Result { get; private set; } = string.Empty;
    public string? RejectionReason { get; private set; }
    public string? Gtin { get; private set; }
    public string? LotNo { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? EpcUri { get; private set; }
    public string? Sscc { get; private set; }
    public decimal? Quantity { get; private set; }
    public string? SkuCode { get; private set; }
    public string? UomCode { get; private set; }
    public string? SiteCode { get; private set; }
    public string? LocationCode { get; private set; }
    public string? QualityStatus { get; private set; }
    public string? OwnerType { get; private set; }
    public string? OwnerId { get; private set; }
    public string? BusinessAction { get; private set; }
    public string? DownstreamEventId { get; private set; }
    public DateTimeOffset ScannedAtUtc { get; private set; }
    public List<EpcisEvent> EpcisEvents { get; private set; } = [];

    public static bool IsSupportedAcceptedWorkflow(string sourceWorkflow)
    {
        var normalized = BarcodeLabelText.Required(sourceWorkflow, nameof(sourceWorkflow)).ToLowerInvariant();
        return SupportedAcceptedWorkflows.Contains(normalized);
    }

    public static bool RequiresInventoryContext(string sourceWorkflow)
    {
        var normalized = BarcodeLabelText.Required(sourceWorkflow, nameof(sourceWorkflow)).ToLowerInvariant();
        return normalized.StartsWith("inventory.", StringComparison.Ordinal);
    }

    public static ScanRecord Record(
        string organizationId,
        string environmentId,
        string deviceCode,
        string scannedValue,
        string sourceWorkflow,
        string sourceDocumentId,
        string idempotencyKey,
        string result,
        string? rejectionReason,
        string? skuCode = null,
        string? uomCode = null,
        string? siteCode = null,
        string? locationCode = null,
        string? qualityStatus = null,
        string? ownerType = null,
        string? ownerId = null,
        decimal? quantity = null)
    {
        return new ScanRecord(
            organizationId,
            environmentId,
            deviceCode,
            scannedValue,
            sourceWorkflow,
            sourceDocumentId,
            idempotencyKey,
            result,
            rejectionReason,
            skuCode,
            uomCode,
            siteCode,
            locationCode,
            qualityStatus,
            ownerType,
            ownerId,
            quantity);
    }

    public bool HasSameIdempotencyPayload(ScanRecord other)
    {
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && DeviceCode == other.DeviceCode
            && ScannedValue == other.ScannedValue
            && SourceWorkflow == other.SourceWorkflow
            && SourceDocumentId == other.SourceDocumentId
            && IdempotencyKey == other.IdempotencyKey
            && Result == other.Result
            && RejectionReason == other.RejectionReason
            && SkuCode == other.SkuCode
            && UomCode == other.UomCode
            && SiteCode == other.SiteCode
            && LocationCode == other.LocationCode
            && QualityStatus == other.QualityStatus
            && OwnerType == other.OwnerType
            && OwnerId == other.OwnerId
            && Quantity == other.Quantity;
    }

    public void EnsureSameIdempotencyPayload(ScanRecord other)
    {
        if (!HasSameIdempotencyPayload(other))
        {
            throw new InvalidOperationException("Scan idempotency key conflicts with a different payload.");
        }
    }

    private void ParseGs1ValueIfPresent()
    {
        if (!ScannedValue.StartsWith("(01)", StringComparison.Ordinal)
            && !ScannedValue.StartsWith("(00)", StringComparison.Ordinal)
            && !(ScannedValue.Length >= 16
                && ScannedValue.StartsWith("01", StringComparison.Ordinal)
                && ScannedValue.Skip(2).Take(14).All(char.IsDigit))
            && !(ScannedValue.Length >= 20
                && ScannedValue.StartsWith("00", StringComparison.Ordinal)
                && ScannedValue.Skip(2).Take(18).All(char.IsDigit)))
        {
            return;
        }

        var parsed = Gs1ApplicationIdentifierParser.Parse(ScannedValue);
        Gtin = parsed.Gtin;
        LotNo = parsed.LotNo;
        SerialNumber = parsed.SerialNumber;
        EpcUri = BarcodeLabelText.Optional(parsed.EpcUri);
        Sscc = parsed.Sscc;
    }

    private void ConfigureBusinessAction()
    {
        BusinessAction = SourceWorkflow switch
        {
            "wms.receiving" => "wms-receiving-scan-observed",
            "production.report" => "production-report-scan-observed",
            "quality.inspection" => "quality-inspection-scan-observed",
            "inventory.count" => "inventory-count-scan-observed",
            "inventory.receipt" or "inventory.issue" or "inventory.adjustment" => "inventory-movement-requested",
            _ => BusinessAction,
        };

        if (BusinessAction is not null)
        {
            DownstreamEventId = $"evt-{Guid.CreateVersion7():N}";
        }

        if (BusinessAction != "inventory-movement-requested")
        {
            return;
        }

        _ = BarcodeLabelText.Required(SkuCode ?? string.Empty, nameof(SkuCode));
        _ = BarcodeLabelText.Required(UomCode ?? string.Empty, nameof(UomCode));
        _ = BarcodeLabelText.Required(SiteCode ?? string.Empty, nameof(SiteCode));
        _ = BarcodeLabelText.Required(LocationCode ?? string.Empty, nameof(LocationCode));
        _ = BarcodeLabelText.Required(QualityStatus ?? string.Empty, nameof(QualityStatus));
        _ = BarcodeLabelText.Required(OwnerType ?? string.Empty, nameof(OwnerType));
        if (Quantity is null or <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Quantity), "Quantity must be positive for inventory scan workflows.");
        }

    }
}
