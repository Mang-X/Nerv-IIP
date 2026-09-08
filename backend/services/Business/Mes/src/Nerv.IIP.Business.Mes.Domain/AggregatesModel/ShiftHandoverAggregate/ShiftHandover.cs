namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;

public partial record ShiftHandoverId : IGuidStronglyTypedId;

public partial record ShiftHandoverWipItemId : IGuidStronglyTypedId;

public partial record ShiftHandoverUnfinishedWorkOrderId : IGuidStronglyTypedId;

public partial record ShiftHandoverOpenIssueId : IGuidStronglyTypedId;

public partial record ShiftHandoverAttachmentId : IGuidStronglyTypedId;

/// <summary>Which shop-floor domain a handed-over open issue came from.</summary>
public enum ShiftHandoverIssueCategory
{
    Equipment = 0,
    Quality = 1,
}

/// <summary>How urgent the outgoing team judged the open issue at handover time.</summary>
public enum ShiftHandoverIssueSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>WIP count line captured at handover time: how much sits on a work order / operation task right now.</summary>
public sealed class ShiftHandoverWipItem : Entity<ShiftHandoverWipItemId>
{
    private ShiftHandoverWipItem()
    {
    }

    private ShiftHandoverWipItem(string workOrderId, string? operationTaskId, decimal quantity)
    {
        Id = new ShiftHandoverWipItemId(Guid.CreateVersion7());
        WorkOrderId = ShiftHandoverGuard.RequiredBounded(workOrderId, nameof(workOrderId), 100);
        OperationTaskId = ShiftHandoverGuard.OptionalBounded(operationTaskId, nameof(operationTaskId), 100);
        Quantity = quantity >= 0
            ? quantity
            : throw new ArgumentOutOfRangeException(nameof(quantity), "在制清点数量不能为负数。");
    }

    /// <summary>MES work-order business id the WIP quantity belongs to.</summary>
    public string WorkOrderId { get; private set; } = string.Empty;

    /// <summary>Operation task the WIP sits on; null when the count is recorded at work-order granularity.</summary>
    public string? OperationTaskId { get; private set; }

    /// <summary>WIP quantity counted at handover time; a snapshot, never recomputed from work orders.</summary>
    public decimal Quantity { get; private set; }

    internal static ShiftHandoverWipItem Create(string workOrderId, string? operationTaskId, decimal quantity) =>
        new(workOrderId, operationTaskId, quantity);
}

/// <summary>Unfinished work order carried into the next shift, with its progress frozen at handover time.</summary>
public sealed class ShiftHandoverUnfinishedWorkOrder : Entity<ShiftHandoverUnfinishedWorkOrderId>
{
    private ShiftHandoverUnfinishedWorkOrder()
    {
    }

    private ShiftHandoverUnfinishedWorkOrder(
        string workOrderId,
        decimal plannedQuantity,
        decimal completedQuantity,
        string workOrderStatus)
    {
        Id = new ShiftHandoverUnfinishedWorkOrderId(Guid.CreateVersion7());
        WorkOrderId = ShiftHandoverGuard.RequiredBounded(workOrderId, nameof(workOrderId), 100);
        PlannedQuantity = plannedQuantity > 0
            ? plannedQuantity
            : throw new ArgumentOutOfRangeException(nameof(plannedQuantity), "未完工单计划数量必须为正数。");
        CompletedQuantity = completedQuantity >= 0
            ? completedQuantity
            : throw new ArgumentOutOfRangeException(nameof(completedQuantity), "未完工单完成数量不能为负数。");
        if (CompletedQuantity >= PlannedQuantity)
        {
            throw new InvalidOperationException("完成数量已达到计划数量的工单不是未完工单。");
        }

        WorkOrderStatus = ShiftHandoverGuard.RequiredBounded(workOrderStatus, nameof(workOrderStatus), 30);
    }

    /// <summary>MES work-order business id carried over to the incoming team.</summary>
    public string WorkOrderId { get; private set; } = string.Empty;

    /// <summary>Work-order planned quantity captured at handover time.</summary>
    public decimal PlannedQuantity { get; private set; }

    /// <summary>Completed quantity captured at handover time; the progress snapshot the incoming team reads.</summary>
    public decimal CompletedQuantity { get; private set; }

    /// <summary>Work-order status captured at handover time.</summary>
    public string WorkOrderStatus { get; private set; } = string.Empty;

    internal static ShiftHandoverUnfinishedWorkOrder Create(
        string workOrderId,
        decimal plannedQuantity,
        decimal completedQuantity,
        string workOrderStatus) =>
        new(workOrderId, plannedQuantity, completedQuantity, workOrderStatus);
}

/// <summary>Equipment or quality problem the outgoing team hands over unresolved.</summary>
public sealed class ShiftHandoverOpenIssue : Entity<ShiftHandoverOpenIssueId>
{
    private ShiftHandoverOpenIssue()
    {
    }

    private ShiftHandoverOpenIssue(
        ShiftHandoverIssueCategory category,
        ShiftHandoverIssueSeverity severity,
        string description,
        string? referenceId)
    {
        Id = new ShiftHandoverOpenIssueId(Guid.CreateVersion7());
        Category = category;
        Severity = severity;
        Description = ShiftHandoverGuard.RequiredBounded(description, nameof(description), 1000);
        ReferenceId = ShiftHandoverGuard.OptionalBounded(referenceId, nameof(referenceId), 100);
    }

    /// <summary>Equipment or quality; the two shop-floor domains a handover carries.</summary>
    public ShiftHandoverIssueCategory Category { get; private set; }

    /// <summary>Severity judged by the outgoing team at handover time.</summary>
    public ShiftHandoverIssueSeverity Severity { get; private set; }

    /// <summary>What the incoming team has to deal with, in the outgoing team's own words.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Optional business id of the originating fact (downtime event, defect record, ...).</summary>
    public string? ReferenceId { get; private set; }

    internal static ShiftHandoverOpenIssue Create(
        ShiftHandoverIssueCategory category,
        ShiftHandoverIssueSeverity severity,
        string description,
        string? referenceId) =>
        new(category, severity, description, referenceId);
}

/// <summary>
/// FileStorage file handed over with the shift — on the shop floor these are phone photos of the
/// machine, the defect or the paperwork the outgoing team is talking about.
///
/// Only the file id crosses back to FileStorage when a download grant is issued; the name, content
/// type and size are snapshots taken at handover time so the read face needs no FileStorage call.
/// </summary>
public sealed class ShiftHandoverAttachment : Entity<ShiftHandoverAttachmentId>
{
    private ShiftHandoverAttachment()
    {
    }

    private ShiftHandoverAttachment(string fileId, string fileName, string contentType, long sizeBytes)
    {
        Id = new ShiftHandoverAttachmentId(Guid.CreateVersion7());
        FileId = ShiftHandoverGuard.RequiredBounded(fileId, nameof(fileId), 150);
        FileName = ShiftHandoverGuard.RequiredBounded(fileName, nameof(fileName), 255);
        ContentType = ShiftHandoverGuard.RequiredBounded(contentType, nameof(contentType), 150);
        SizeBytes = sizeBytes >= 0
            ? sizeBytes
            : throw new ArgumentOutOfRangeException(nameof(sizeBytes), "交接班附件大小不能为负数。");
    }

    /// <summary>FileStorage file id; the only handle a download grant needs.</summary>
    public string FileId { get; private set; } = string.Empty;

    /// <summary>File name captured at handover time.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Content type captured at handover time; tells the read face whether it can be shown inline.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>File size in bytes captured at handover time.</summary>
    public long SizeBytes { get; private set; }

    internal static ShiftHandoverAttachment Create(string fileId, string fileName, string contentType, long sizeBytes) =>
        new(fileId, fileName, contentType, sizeBytes);
}

/// <summary>WIP count line supplied by the write face.</summary>
public sealed record ShiftHandoverWipItemSnapshot(string WorkOrderId, string? OperationTaskId, decimal Quantity);

/// <summary>Unfinished work-order progress supplied by the write face.</summary>
public sealed record ShiftHandoverUnfinishedWorkOrderSnapshot(
    string WorkOrderId,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    string WorkOrderStatus);

/// <summary>Open issue supplied by the write face.</summary>
public sealed record ShiftHandoverOpenIssueSnapshot(
    ShiftHandoverIssueCategory Category,
    ShiftHandoverIssueSeverity Severity,
    string Description,
    string? ReferenceId = null);

/// <summary>FileStorage attachment supplied by the write face.</summary>
public sealed record ShiftHandoverAttachmentSnapshot(
    string FileId,
    string FileName,
    string ContentType,
    long SizeBytes);

public sealed class ShiftHandover : Entity<ShiftHandoverId>, IAggregateRoot
{
    public const string OpenStatus = "Open";
    public const string AcceptedStatus = "Accepted";

    private readonly List<ShiftHandoverWipItem> wipItems = [];
    private readonly List<ShiftHandoverUnfinishedWorkOrder> unfinishedWorkOrders = [];
    private readonly List<ShiftHandoverOpenIssue> openIssues = [];
    private readonly List<ShiftHandoverAttachment> attachments = [];

    private ShiftHandover()
    {
    }

    private ShiftHandover(
        string organizationId,
        string environmentId,
        string handoverNo,
        string shiftId,
        string teamId,
        int openIssueCount,
        DateTimeOffset createdAtUtc,
        string? teamName,
        string? outgoingUserId,
        string? outgoingUserName)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        HandoverNo = DomainGuard.Required(handoverNo, nameof(handoverNo));
        ShiftId = DomainGuard.Required(shiftId, nameof(shiftId));
        TeamId = DomainGuard.Required(teamId, nameof(teamId));
        TeamName = string.IsNullOrWhiteSpace(teamName) ? null : teamName.Trim();
        OutgoingUserId = ShiftHandoverGuard.OptionalBounded(outgoingUserId, nameof(outgoingUserId), 200);
        OutgoingUserName = ShiftHandoverGuard.OptionalBounded(outgoingUserName, nameof(outgoingUserName), 200);
        OpenIssueCount = openIssueCount >= 0
            ? openIssueCount
            : throw new ArgumentOutOfRangeException(nameof(openIssueCount), "Open issue count cannot be negative.");
        HandoverStatus = OpenStatus;
        CreatedAtUtc = createdAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string HandoverNo { get; private set; } = string.Empty;
    /// <summary>
    /// MasterData shift public id (e.g. EARLY / MIDDLE) — the working window being handed over.
    /// Distinct from <see cref="TeamId"/>: a shift is *when*, a team is *who*.
    /// </summary>
    public string ShiftId { get; private set; } = string.Empty;

    /// <summary>MasterData team public id (e.g. TEAM-WB-MC-A) — never a display name.</summary>
    public string TeamId { get; private set; } = string.Empty;

    /// <summary>Display name of the team captured at handover time; snapshot for the read face.</summary>
    public string? TeamName { get; private set; }

    /// <summary>Identity of the worker handing the shift over.</summary>
    public string? OutgoingUserId { get; private set; }

    /// <summary>Display name of the outgoing worker captured at handover time; snapshot like <see cref="TeamName"/>.</summary>
    public string? OutgoingUserName { get; private set; }

    /// <summary>Identity of the worker taking the shift over; written when the handover is accepted.</summary>
    public string? IncomingUserId { get; private set; }

    /// <summary>Display name of the incoming worker captured at acceptance time.</summary>
    public string? IncomingUserName { get; private set; }

    public string HandoverStatus { get; private set; } = string.Empty;

    /// <summary>
    /// Environment-level count of still-open shop-floor facts derived when the handover was created.
    /// Kept as-is for the existing read face; it is a derived total and is not the size of
    /// <see cref="OpenIssues"/>, which is what the outgoing team explicitly wrote down.
    /// </summary>
    public int OpenIssueCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public IReadOnlyCollection<ShiftHandoverWipItem> WipItems => wipItems;
    public IReadOnlyCollection<ShiftHandoverUnfinishedWorkOrder> UnfinishedWorkOrders => unfinishedWorkOrders;
    public IReadOnlyCollection<ShiftHandoverOpenIssue> OpenIssues => openIssues;
    public IReadOnlyCollection<ShiftHandoverAttachment> Attachments => attachments;

    public static ShiftHandover Create(
        string organizationId,
        string environmentId,
        string handoverNo,
        string shiftId,
        string teamId,
        int openIssueCount,
        DateTimeOffset createdAtUtc,
        string? teamName = null,
        string? outgoingUserId = null,
        string? outgoingUserName = null,
        IReadOnlyCollection<ShiftHandoverWipItemSnapshot>? wipItems = null,
        IReadOnlyCollection<ShiftHandoverUnfinishedWorkOrderSnapshot>? unfinishedWorkOrders = null,
        IReadOnlyCollection<ShiftHandoverOpenIssueSnapshot>? openIssues = null,
        IReadOnlyCollection<ShiftHandoverAttachmentSnapshot>? attachments = null)
    {
        var handover = new ShiftHandover(
            organizationId,
            environmentId,
            handoverNo,
            shiftId,
            teamId,
            openIssueCount,
            createdAtUtc,
            teamName,
            outgoingUserId,
            outgoingUserName);

        foreach (var item in wipItems ?? [])
        {
            handover.wipItems.Add(ShiftHandoverWipItem.Create(item.WorkOrderId, item.OperationTaskId, item.Quantity));
        }

        foreach (var workOrder in unfinishedWorkOrders ?? [])
        {
            handover.unfinishedWorkOrders.Add(ShiftHandoverUnfinishedWorkOrder.Create(
                workOrder.WorkOrderId,
                workOrder.PlannedQuantity,
                workOrder.CompletedQuantity,
                workOrder.WorkOrderStatus));
        }

        foreach (var issue in openIssues ?? [])
        {
            handover.openIssues.Add(ShiftHandoverOpenIssue.Create(
                issue.Category,
                issue.Severity,
                issue.Description,
                issue.ReferenceId));
        }

        foreach (var attachment in attachments ?? [])
        {
            handover.attachments.Add(ShiftHandoverAttachment.Create(
                attachment.FileId,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes));
        }

        return handover;
    }

    public void Accept(DateTimeOffset acceptedAtUtc, string? incomingUserId = null, string? incomingUserName = null)
    {
        if (HandoverStatus == AcceptedStatus)
        {
            return;
        }

        if (HandoverStatus != OpenStatus)
        {
            throw new InvalidOperationException("Only open shift handover can be accepted.");
        }

        HandoverStatus = AcceptedStatus;
        AcceptedAtUtc = acceptedAtUtc;
        IncomingUserId = ShiftHandoverGuard.OptionalBounded(incomingUserId, nameof(incomingUserId), 200);
        IncomingUserName = ShiftHandoverGuard.OptionalBounded(incomingUserName, nameof(incomingUserName), 200);
    }
}

internal static class ShiftHandoverGuard
{
    internal static string RequiredBounded(string value, string parameterName, int maxLength)
    {
        var normalized = DomainGuard.Required(value, parameterName);
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
    }

    internal static string? OptionalBounded(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
    }
}
