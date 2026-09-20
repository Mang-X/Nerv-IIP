using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public enum BusinessConsoleMesAndonCategory { MaterialShortage, Equipment, Quality, Process }
public enum BusinessConsoleMesAndonStatus { Open, Claimed, Closed }
public enum BusinessConsoleMesAndonQueue { AwaitingResponse, Unclosed, All }

public interface IBusinessConsoleMesAndonScopeRequest
{
    string OrganizationId { get; }
    string EnvironmentId { get; }
    string? ScopeKind { get; }
    string? ScopeId { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class BusinessConsoleMesRaiseAndonCallRequest : IBusinessConsoleMesAndonScopeRequest
{
    public string OrganizationId { get; init; } = string.Empty;
    public string EnvironmentId { get; init; } = string.Empty;
    public string? ScopeKind { get; init; }
    public string? ScopeId { get; init; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public BusinessConsoleMesAndonCategory Category { get; init; }
    public string WorkOrderId { get; init; } = string.Empty;
    public string OperationTaskId { get; init; } = string.Empty;
    public string WorkCenterId { get; init; } = string.Empty;
}

public sealed class BusinessConsoleMesAndonCallRequest : IBusinessConsoleMesAndonScopeRequest
{
    [RouteParam] public Guid Id { get; init; }
    public string OrganizationId { get; init; } = string.Empty;
    public string EnvironmentId { get; init; } = string.Empty;
    public string? ScopeKind { get; init; }
    public string? ScopeId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class BusinessConsoleMesAndonCallActionRequest : IBusinessConsoleMesAndonScopeRequest
{
    [RouteParam] public Guid Id { get; init; }
    public string OrganizationId { get; init; } = string.Empty;
    public string EnvironmentId { get; init; } = string.Empty;
    public string? ScopeKind { get; init; }
    public string? ScopeId { get; init; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class BusinessConsoleMesListAndonCallsRequest : IBusinessConsoleMesAndonScopeRequest
{
    public string OrganizationId { get; init; } = string.Empty;
    public string EnvironmentId { get; init; } = string.Empty;
    public string? ScopeKind { get; init; }
    public string? ScopeId { get; init; }
    public BusinessConsoleMesAndonQueue Queue { get; init; } = BusinessConsoleMesAndonQueue.Unclosed;
    public BusinessConsoleMesAndonCategory? Category { get; init; }
    public string? WorkCenterId { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}

public sealed record BusinessConsoleMesAndonCallResponse(
    string Id, string OrganizationId, string EnvironmentId,
    BusinessConsoleMesAndonCategory Category, BusinessConsoleMesAndonStatus Status,
    string WorkOrderId, string OperationTaskId, string WorkCenterId, string CallerId,
    DateTimeOffset RaisedAtUtc, string? ResponderId, DateTimeOffset? FirstRespondedAtUtc,
    double? ResponseDurationSeconds, DateTimeOffset? ClosedAtUtc,
    DateTimeOffset? EscalatedAtUtc, string? EscalationRecipientId);

public sealed record BusinessConsoleMesAndonCallListResponse(
    IReadOnlyCollection<BusinessConsoleMesAndonCallResponse> Items, int Total);

internal static class BusinessConsoleMesAndonJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
