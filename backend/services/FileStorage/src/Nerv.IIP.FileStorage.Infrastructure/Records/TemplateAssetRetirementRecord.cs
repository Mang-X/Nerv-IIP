using Nerv.IIP.FileStorage.Domain;

namespace Nerv.IIP.FileStorage.Infrastructure.Records;

/// <summary>Durable lifecycle receipt, deliberately independent of the stored-file row's lifetime.</summary>
public sealed class TemplateAssetRetirementRecord
{
    private TemplateAssetRetirementRecord() { }

    public string DecisionId { get; private set; } = string.Empty;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string FileId { get; private set; } = string.Empty;
    public string Checksum { get; private set; } = string.Empty;
    public string OwnerService { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Status { get; private set; } = FileStorageFileStatus.PhysicalHold;
    public DateTimeOffset AcceptedAtUtc { get; private set; }
    public long ReplayPolicyVersion { get; private set; }
    public long ClientWindowSeconds { get; private set; }
    public long BarcodeLeaseSeconds { get; private set; }
    public long BarcodeMaxBackoffSeconds { get; private set; }
    public long PhysicalGraceSeconds { get; private set; }
    public long GcIntervalSeconds { get; private set; }
    public long StorageLeaseSeconds { get; private set; }
    public long StorageMaxBackoffSeconds { get; private set; }
    public long ReplayHorizonSeconds { get; private set; }

    public static TemplateAssetRetirementRecord Accept(RetirementCapability request,
        RetirementStorageInputs storage, long sizeBytes, DateTimeOffset now) => new()
    {
        DecisionId = request.DecisionId, OrganizationId = request.OrganizationId,
        EnvironmentId = request.EnvironmentId, FileId = request.FileId, Checksum = request.Checksum,
        OwnerService = request.OwnerService, OwnerType = request.OwnerType, OwnerId = request.OwnerId,
        Purpose = request.Purpose, SizeBytes = sizeBytes, AcceptedAtUtc = now,
        ReplayPolicyVersion = request.ReplayPolicyVersion, ClientWindowSeconds = request.ClientWindowSeconds,
        BarcodeLeaseSeconds = request.BarcodeLeaseSeconds, BarcodeMaxBackoffSeconds = request.BarcodeMaxBackoffSeconds,
        PhysicalGraceSeconds = storage.PhysicalGraceSeconds, GcIntervalSeconds = storage.GcIntervalSeconds,
        StorageLeaseSeconds = storage.LeaseSeconds, StorageMaxBackoffSeconds = storage.MaxBackoffSeconds,
        ReplayHorizonSeconds = RetirementReplayPolicy.Resolve(request.ClientWindowSeconds,
            request.BarcodeLeaseSeconds, request.BarcodeMaxBackoffSeconds, storage)
    };

    public bool Matches(RetirementCapability request) =>
        DecisionId == request.DecisionId && OrganizationId == request.OrganizationId
        && EnvironmentId == request.EnvironmentId && FileId == request.FileId && Checksum == request.Checksum
        && OwnerService == request.OwnerService && OwnerType == request.OwnerType && OwnerId == request.OwnerId
        && Purpose == request.Purpose && ReplayPolicyVersion == request.ReplayPolicyVersion
        && ClientWindowSeconds == request.ClientWindowSeconds && BarcodeLeaseSeconds == request.BarcodeLeaseSeconds
        && BarcodeMaxBackoffSeconds == request.BarcodeMaxBackoffSeconds;
}
