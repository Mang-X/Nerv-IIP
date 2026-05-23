using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

public partial record ScanRecordId : IGuidStronglyTypedId;

public sealed class ScanRecord : Entity<ScanRecordId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedResults = ["accepted", "rejected"];

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
        string? rejectionReason)
    {
        Id = new ScanRecordId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        DeviceCode = BarcodeLabelText.Required(deviceCode, nameof(deviceCode));
        ScannedValue = BarcodeLabelText.Required(scannedValue, nameof(scannedValue));
        SourceWorkflow = BarcodeLabelText.Required(sourceWorkflow, nameof(sourceWorkflow));
        SourceDocumentId = BarcodeLabelText.Required(sourceDocumentId, nameof(sourceDocumentId));
        IdempotencyKey = BarcodeLabelText.Required(idempotencyKey, nameof(idempotencyKey));
        Result = BarcodeLabelText.Supported(result, SupportedResults, nameof(result));
        RejectionReason = BarcodeLabelText.Optional(rejectionReason);
        ScannedAtUtc = DateTimeOffset.UtcNow;

        if (Result == "rejected")
        {
            this.AddDomainEvent(new ScanRejectedDomainEvent(this));
        }
        else
        {
            this.AddDomainEvent(new LabelScannedDomainEvent(this));
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
    public DateTimeOffset ScannedAtUtc { get; private set; }

    public static ScanRecord Record(
        string organizationId,
        string environmentId,
        string deviceCode,
        string scannedValue,
        string sourceWorkflow,
        string sourceDocumentId,
        string idempotencyKey,
        string result,
        string? rejectionReason)
    {
        return new ScanRecord(organizationId, environmentId, deviceCode, scannedValue, sourceWorkflow, sourceDocumentId, idempotencyKey, result, rejectionReason);
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
            && RejectionReason == other.RejectionReason;
    }

    public void EnsureSameIdempotencyPayload(ScanRecord other)
    {
        if (!HasSameIdempotencyPayload(other))
        {
            throw new InvalidOperationException("Scan idempotency key conflicts with a different payload.");
        }
    }
}
