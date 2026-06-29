using System.Text.Json;
using Nerv.IIP.Iam.Domain.AggregatesModel.SecurityAuditAggregate;
using Nerv.IIP.Iam.Infrastructure.Repositories;

namespace Nerv.IIP.Iam.Web.Application.SecurityAudit;

public sealed record SecurityAuditContext(
    string Actor,
    string CorrelationId,
    string? SourceIp,
    string OrganizationId,
    string EnvironmentId);

public sealed record SecurityAuditRecordResponse(
    string SecurityAuditRecordId,
    string OrganizationId,
    string EnvironmentId,
    string Action,
    string Actor,
    string TargetType,
    string TargetId,
    string Outcome,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? SourceIp,
    string DetailsJson);

public sealed record SecurityAuditListOptions(
    string? OrganizationId,
    string? EnvironmentId,
    string? Action,
    string? TargetType,
    string? TargetId,
    DateTimeOffset? OccurredFromUtc,
    DateTimeOffset? OccurredToUtc,
    int? Take);

public interface ISecurityAuditRecorder
{
    Task RecordAsync(
        SecurityAuditContext context,
        string action,
        string targetType,
        string targetId,
        string outcome,
        object details,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);

    Task RecordAndSaveAsync(
        SecurityAuditContext context,
        string action,
        string targetType,
        string targetId,
        string outcome,
        object details,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);
}

public sealed class SecurityAuditRecorder(ISecurityAuditRepository repository) : ISecurityAuditRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(
        SecurityAuditContext context,
        string action,
        string targetType,
        string targetId,
        string outcome,
        object details,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await repository.AddAsync(CreateRecord(context, action, targetType, targetId, outcome, details, occurredAtUtc), cancellationToken);
    }

    public async Task RecordAndSaveAsync(
        SecurityAuditContext context,
        string action,
        string targetType,
        string targetId,
        string outcome,
        object details,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await repository.AddAsync(CreateRecord(context, action, targetType, targetId, outcome, details, occurredAtUtc), cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static SecurityAuditRecord CreateRecord(
        SecurityAuditContext context,
        string action,
        string targetType,
        string targetId,
        string outcome,
        object details,
        DateTimeOffset occurredAtUtc)
    {
        return new SecurityAuditRecord(
            new SecurityAuditRecordId($"audit-{Guid.CreateVersion7():N}"),
            Normalize(context.OrganizationId, "unknown"),
            Normalize(context.EnvironmentId, "unknown"),
            action,
            Normalize(context.Actor, "unknown"),
            targetType,
            Normalize(targetId, "unknown"),
            outcome,
            occurredAtUtc,
            Normalize(context.CorrelationId, Guid.CreateVersion7().ToString("N")),
            context.SourceIp,
            JsonSerializer.Serialize(details, JsonOptions));
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

public interface IIamSecurityAuditApplicationService
{
    Task<IReadOnlyList<SecurityAuditRecordResponse>> ListAsync(
        SecurityAuditListOptions options,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlIamSecurityAuditApplicationService(ISecurityAuditRepository repository)
    : IIamSecurityAuditApplicationService
{
    public async Task<IReadOnlyList<SecurityAuditRecordResponse>> ListAsync(
        SecurityAuditListOptions options,
        CancellationToken cancellationToken)
    {
        var records = await repository.ListAsync(
            options.OrganizationId,
            options.EnvironmentId,
            options.Action,
            options.TargetType,
            options.TargetId,
            options.OccurredFromUtc,
            options.OccurredToUtc,
            options.Take ?? 50,
            cancellationToken);
        return records.Select(ToResponse).ToArray();
    }

    private static SecurityAuditRecordResponse ToResponse(SecurityAuditRecord record)
    {
        return new SecurityAuditRecordResponse(
            record.Id.Id,
            record.OrganizationId,
            record.EnvironmentId,
            record.Action,
            record.Actor,
            record.TargetType,
            record.TargetId,
            record.Outcome,
            record.OccurredAtUtc,
            record.CorrelationId,
            record.SourceIp,
            record.DetailsJson);
    }
}

public sealed class InMemoryIamSecurityAuditApplicationService : IIamSecurityAuditApplicationService
{
    public Task<IReadOnlyList<SecurityAuditRecordResponse>> ListAsync(
        SecurityAuditListOptions options,
        CancellationToken cancellationToken)
    {
        _ = options;
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<SecurityAuditRecordResponse>>([]);
    }
}
