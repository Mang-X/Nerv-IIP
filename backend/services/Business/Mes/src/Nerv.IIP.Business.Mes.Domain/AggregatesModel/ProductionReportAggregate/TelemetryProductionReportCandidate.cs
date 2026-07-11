namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record TelemetryProductionReportCandidateId : IGuidStronglyTypedId;

public sealed class TelemetryProductionReportCandidate : Entity<TelemetryProductionReportCandidateId>, IAggregateRoot
{
    public const string DraftReportingMode = "draft";
    public const string PostedReportingMode = "posted";
    public const string DraftStatus = "draft";
    public const string PendingConfirmationStatus = "pending-confirmation";
    public const string ActiveAlarmSuspensionReason = "active-alarm";
    public const string NoWorkCenterMappingSuspensionReason = "no-work-center-mapping";
    public const string NoCurrentWorkOrderSuspensionReason = "no-current-work-order";

    private TelemetryProductionReportCandidate()
    {
    }

    private TelemetryProductionReportCandidate(
        string organizationId,
        string environmentId,
        string sourceIdempotencyKey,
        string deviceAssetId,
        string tagKey,
        string reportingMode,
        decimal goodQuantity,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset bucketEndUtc,
        string? workCenterId,
        string? workOrderId,
        string? operationTaskId,
        string status,
        string? suspensionReason)
    {
        if (goodQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(goodQuantity), "Telemetry production count candidate must be positive.");
        }

        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        SourceIdempotencyKey = DomainGuard.Required(sourceIdempotencyKey, nameof(sourceIdempotencyKey));
        DeviceAssetId = DomainGuard.Required(deviceAssetId, nameof(deviceAssetId));
        TagKey = DomainGuard.Required(tagKey, nameof(tagKey)).ToLowerInvariant();
        ReportingMode = NormalizeReportingMode(reportingMode);
        GoodQuantity = goodQuantity;
        BucketStartUtc = bucketStartUtc;
        BucketEndUtc = bucketEndUtc > bucketStartUtc
            ? bucketEndUtc
            : throw new ArgumentOutOfRangeException(nameof(bucketEndUtc), "Telemetry count bucket end must be after its start.");
        WorkCenterId = NormalizeOptional(workCenterId);
        WorkOrderId = NormalizeOptional(workOrderId);
        OperationTaskId = NormalizeOptional(operationTaskId);
        Status = NormalizeStatus(status);
        SuspensionReason = NormalizeOptional(suspensionReason);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SourceIdempotencyKey { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string TagKey { get; private set; } = string.Empty;
    public string ReportingMode { get; private set; } = string.Empty;
    public decimal GoodQuantity { get; private set; }
    public DateTimeOffset BucketStartUtc { get; private set; }
    public DateTimeOffset BucketEndUtc { get; private set; }
    public string? WorkCenterId { get; private set; }
    public string? WorkOrderId { get; private set; }
    public string? OperationTaskId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TelemetryProductionReportCandidate CreateDraft(
        string organizationId,
        string environmentId,
        string sourceIdempotencyKey,
        string deviceAssetId,
        string tagKey,
        decimal goodQuantity,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset bucketEndUtc,
        string workCenterId,
        string workOrderId,
        string operationTaskId)
    {
        return new TelemetryProductionReportCandidate(
            organizationId,
            environmentId,
            sourceIdempotencyKey,
            deviceAssetId,
            tagKey,
            DraftReportingMode,
            goodQuantity,
            bucketStartUtc,
            bucketEndUtc,
            workCenterId,
            workOrderId,
            operationTaskId,
            DraftStatus,
            null);
    }

    public static TelemetryProductionReportCandidate CreatePendingConfirmation(
        string organizationId,
        string environmentId,
        string sourceIdempotencyKey,
        string deviceAssetId,
        string tagKey,
        string reportingMode,
        decimal goodQuantity,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset bucketEndUtc,
        string? workCenterId,
        string? workOrderId,
        string? operationTaskId,
        string suspensionReason)
    {
        return new TelemetryProductionReportCandidate(
            organizationId,
            environmentId,
            sourceIdempotencyKey,
            deviceAssetId,
            tagKey,
            reportingMode,
            goodQuantity,
            bucketStartUtc,
            bucketEndUtc,
            workCenterId,
            workOrderId,
            operationTaskId,
            PendingConfirmationStatus,
            suspensionReason);
    }

    private static string NormalizeReportingMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        PostedReportingMode => PostedReportingMode,
        DraftReportingMode => DraftReportingMode,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Telemetry reporting mode must be posted or draft."),
    };

    private static string NormalizeStatus(string value) => value.Trim().ToLowerInvariant() switch
    {
        DraftStatus => DraftStatus,
        PendingConfirmationStatus => PendingConfirmationStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported telemetry report candidate status."),
    };

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
