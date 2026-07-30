namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

public partial record InspectionTaskAssignmentReceiptId : IGuidStronglyTypedId;

public sealed class InspectionTaskAssignmentReceipt
    : Entity<InspectionTaskAssignmentReceiptId>, IAggregateRoot
{
    private InspectionTaskAssignmentReceipt()
    {
    }

    private InspectionTaskAssignmentReceipt(
        string organizationId,
        string environmentId,
        InspectionTaskId inspectionTaskId,
        string action,
        string idempotencyKey,
        string payloadFingerprint,
        string actorPrincipalId,
        string? previousInspectorUserId,
        string? previousTeamId,
        string? assignedInspectorUserId,
        string? assignedTeamId,
        string? reason,
        long resultVersion,
        DateTimeOffset createdAtUtc)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        InspectionTaskId = inspectionTaskId;
        Action = Required(action).ToLowerInvariant();
        IdempotencyKey = Required(idempotencyKey);
        PayloadFingerprint = Required(payloadFingerprint);
        ActorPrincipalId = Required(actorPrincipalId);
        PreviousInspectorUserId = Optional(previousInspectorUserId);
        PreviousTeamId = Optional(previousTeamId);
        AssignedInspectorUserId = Optional(assignedInspectorUserId);
        AssignedTeamId = Optional(assignedTeamId);
        Reason = Optional(reason);
        ResultVersion = resultVersion > 0
            ? resultVersion
            : throw new ArgumentOutOfRangeException(nameof(resultVersion));
        CreatedAtUtc = createdAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public InspectionTaskId InspectionTaskId { get; private set; } = null!;
    public string Action { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public string ActorPrincipalId { get; private set; } = string.Empty;
    public string? PreviousInspectorUserId { get; private set; }
    public string? PreviousTeamId { get; private set; }
    public string? AssignedInspectorUserId { get; private set; }
    public string? AssignedTeamId { get; private set; }
    public string? Reason { get; private set; }
    public long ResultVersion { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static InspectionTaskAssignmentReceipt Create(
        string organizationId,
        string environmentId,
        InspectionTaskId inspectionTaskId,
        string action,
        string idempotencyKey,
        string payloadFingerprint,
        string actorPrincipalId,
        string? previousInspectorUserId,
        string? previousTeamId,
        string? assignedInspectorUserId,
        string? assignedTeamId,
        string? reason,
        long resultVersion,
        DateTimeOffset createdAtUtc) =>
        new(
            organizationId,
            environmentId,
            inspectionTaskId,
            action,
            idempotencyKey,
            payloadFingerprint,
            actorPrincipalId,
            previousInspectorUserId,
            previousTeamId,
            assignedInspectorUserId,
            assignedTeamId,
            reason,
            resultVersion,
            createdAtUtc);

    public bool MatchesPayload(string payloadFingerprint) =>
        string.Equals(PayloadFingerprint, Required(payloadFingerprint), StringComparison.Ordinal);

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", nameof(value))
            : value.Trim();

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
