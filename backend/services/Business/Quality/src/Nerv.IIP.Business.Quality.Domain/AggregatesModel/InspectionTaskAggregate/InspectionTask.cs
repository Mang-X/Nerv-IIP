using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

public partial record InspectionTaskId : IGuidStronglyTypedId;

public sealed class InspectionTask : Entity<InspectionTaskId>, IAggregateRoot
{
    private static readonly HashSet<string> SourceTypes = ["receiving", "operation", "final"];
    private static readonly HashSet<string> SourceServices = ["wms", "erp", "mes"];

    private InspectionTask()
    {
    }

    private InspectionTask(
        string organizationId,
        string environmentId,
        InspectionPlanId inspectionPlanId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string skuCode,
        decimal quantity,
        string uomCode,
        string? batchNo,
        string? serialNo,
        DateTimeOffset createdAtUtc,
        DateTimeOffset dueAtUtc,
        string triggerIdempotencyKey)
    {
        Id = new InspectionTaskId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        InspectionPlanId = inspectionPlanId;
        SourceType = Supported(sourceType, SourceTypes, nameof(sourceType));
        SourceService = Supported(sourceService, SourceServices, nameof(sourceService));
        SourceDocumentId = Required(sourceDocumentId);
        SourceDocumentLineId = Optional(sourceDocumentLineId);
        SkuCode = Required(skuCode);
        Quantity = Positive(quantity, nameof(quantity));
        UomCode = Required(uomCode);
        BatchNo = Optional(batchNo);
        SerialNo = Optional(serialNo);
        Status = InspectionTaskStatuses.Pending;
        Version = 1;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        DueAtUtc = dueAtUtc;
        TriggerIdempotencyKey = Required(triggerIdempotencyKey);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public InspectionPlanId InspectionPlanId { get; private set; } = null!;
    public InspectionRecordId? InspectionRecordId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public string SourceService { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string? SourceDocumentLineId { get; private set; }
    public string SkuCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public string? BatchNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? AssignedUserId { get; private set; }
    public string? AssignedTeamId { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset DueAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? OverdueReminderSentAtUtc { get; private set; }
    public string TriggerIdempotencyKey { get; private set; } = string.Empty;

    public static InspectionTask CreatePending(
        string organizationId,
        string environmentId,
        InspectionPlanId inspectionPlanId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string skuCode,
        decimal quantity,
        string uomCode,
        string? batchNo,
        string? serialNo,
        DateTimeOffset createdAtUtc,
        DateTimeOffset dueAtUtc,
        string triggerIdempotencyKey)
    {
        if (dueAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(dueAtUtc), "Due time must be after created time.");
        }

        return new InspectionTask(
            organizationId,
            environmentId,
            inspectionPlanId,
            sourceType,
            sourceService,
            sourceDocumentId,
            sourceDocumentLineId,
            skuCode,
            quantity,
            uomCode,
            batchNo,
            serialNo,
            createdAtUtc,
            dueAtUtc,
            triggerIdempotencyKey);
    }

    public void Start(string assignedUserId, DateTimeOffset startedAtUtc)
    {
        if (Status != InspectionTaskStatuses.Pending)
        {
            throw new InvalidOperationException("Only pending inspection tasks can be started.");
        }

        var normalizedUserId = Required(assignedUserId);
        if (AssignedTeamId is not null && AssignedUserId is null)
        {
            throw new UnauthorizedAccessException("Team-assigned inspection tasks must be claimed with an authorized team.");
        }

        if (AssignedUserId is not null
            && !string.Equals(AssignedUserId, normalizedUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Inspection task is assigned to another inspector.");
        }

        AssignedUserId = normalizedUserId;
        StartedAtUtc = startedAtUtc;
        Status = InspectionTaskStatuses.InProgress;
        Version++;
        Touch(startedAtUtc);
    }

    public void Assign(
        string? assignedUserId,
        string? assignedTeamId,
        long expectedVersion,
        DateTimeOffset assignedAtUtc)
    {
        EnsurePending();
        EnsureExpectedVersion(expectedVersion);
        var normalizedUserId = Optional(assignedUserId);
        var normalizedTeamId = Optional(assignedTeamId);
        if (normalizedUserId is null && normalizedTeamId is null)
        {
            throw new ArgumentException("An inspector or team assignment is required.");
        }

        AssignedUserId = normalizedUserId;
        AssignedTeamId = normalizedTeamId;
        Version++;
        Touch(assignedAtUtc);
    }

    public void Claim(
        string inspectorUserId,
        IReadOnlyCollection<string> authorizedTeamIds,
        long expectedVersion,
        DateTimeOffset claimedAtUtc)
    {
        EnsurePending();
        EnsureExpectedVersion(expectedVersion);
        var normalizedUserId = Required(inspectorUserId);
        if (AssignedUserId is not null
            && !string.Equals(AssignedUserId, normalizedUserId, StringComparison.Ordinal))
        {
            throw new InspectionTaskAlreadyClaimedException();
        }

        if (AssignedTeamId is not null
            && !authorizedTeamIds.Any(teamId =>
                string.Equals(Optional(teamId), AssignedTeamId, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException("Inspection task is outside the inspector's authorized teams.");
        }

        if (AssignedUserId is null && AssignedTeamId is null)
        {
            throw new UnauthorizedAccessException("Inspection task has no authorized assignment.");
        }

        AssignedUserId = normalizedUserId;
        StartedAtUtc = claimedAtUtc;
        Status = InspectionTaskStatuses.InProgress;
        Version++;
        Touch(claimedAtUtc);
    }

    public void EnsureAssignedInspector(string inspectorUserId)
    {
        if (!string.Equals(
                AssignedUserId,
                Required(inspectorUserId),
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Inspection task is assigned to another inspector.");
        }
    }

    public void Complete(InspectionRecordId inspectionRecordId, DateTimeOffset completedAtUtc)
    {
        if (Status != InspectionTaskStatuses.InProgress)
        {
            throw new InvalidOperationException("Only in-progress inspection tasks can be completed.");
        }

        InspectionRecordId = inspectionRecordId;
        CompletedAtUtc = completedAtUtc;
        Status = InspectionTaskStatuses.Completed;
        Version++;
        Touch(completedAtUtc);
    }

    public void MarkOverdueReminderSent(DateTimeOffset remindedAtUtc)
    {
        if (Status == InspectionTaskStatuses.Completed)
        {
            return;
        }

        OverdueReminderSentAtUtc ??= remindedAtUtc;
        Touch(remindedAtUtc);
    }

    private void Touch(DateTimeOffset changedAtUtc)
    {
        UpdatedAtUtc = changedAtUtc;
    }

    private void EnsurePending()
    {
        if (Status != InspectionTaskStatuses.Pending)
        {
            throw new InvalidOperationException("Only pending inspection tasks can change assignment.");
        }
    }

    private void EnsureExpectedVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new InvalidOperationException(
                $"Inspection task version conflict. Expected {expectedVersion}, current {Version}.");
        }
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0m ? throw new ArgumentOutOfRangeException(parameterName, "Value must be positive.") : value;
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

public sealed class InspectionTaskAlreadyClaimedException()
    : InvalidOperationException("Inspection task has already been assigned to another inspector.");

public static class InspectionTaskStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";
}
