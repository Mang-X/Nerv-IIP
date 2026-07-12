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

        AssignedUserId = Required(assignedUserId);
        StartedAtUtc = startedAtUtc;
        Status = InspectionTaskStatuses.InProgress;
        Touch(startedAtUtc);
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

public static class InspectionTaskStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";
}
