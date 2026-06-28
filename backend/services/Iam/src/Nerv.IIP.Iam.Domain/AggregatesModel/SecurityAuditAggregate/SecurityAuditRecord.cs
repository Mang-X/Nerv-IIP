using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.SecurityAuditAggregate;

public partial record SecurityAuditRecordId : IStringStronglyTypedId;

public sealed class SecurityAuditRecord : Entity<SecurityAuditRecordId>, IAggregateRoot
{
    private SecurityAuditRecord()
    {
        Id = new SecurityAuditRecordId(string.Empty);
    }

    public SecurityAuditRecord(
        SecurityAuditRecordId id,
        string organizationId,
        string environmentId,
        string action,
        string actor,
        string targetType,
        string targetId,
        string outcome,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        string? sourceIp,
        string detailsJson)
    {
        Id = id;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        Action = action;
        Actor = actor;
        TargetType = targetType;
        TargetId = targetId;
        Outcome = outcome;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        SourceIp = sourceIp;
        DetailsJson = detailsJson;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string TargetId { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? SourceIp { get; private set; }
    public string DetailsJson { get; private set; } = "{}";
}
