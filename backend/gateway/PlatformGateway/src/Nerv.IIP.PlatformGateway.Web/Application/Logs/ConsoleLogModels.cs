using Nerv.IIP.Observability;

namespace Nerv.IIP.PlatformGateway.Web.Application.Logs;

public sealed class ConsoleLogQueryRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Service { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? Level { get; set; }
    public string? Text { get; set; }
    public int? Limit { get; set; }
    public int? Cursor { get; set; }
}

public sealed record ConsoleLogQueryResponse(
    IReadOnlyList<ConsoleLogEntryResponse> Items,
    int? NextCursor,
    bool Partial,
    string BackendStatus);

public sealed record ConsoleLogEntryResponse(
    DateTimeOffset Timestamp,
    string Level,
    string Service,
    string Message,
    string? InstanceKey,
    string? OperationTaskId,
    string? CorrelationId,
    string? TraceId,
    string Source,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Fields)
{
    public static ConsoleLogEntryResponse FromVictoriaLogs(VictoriaLogsLogEntry entry) =>
        new(
            entry.Timestamp,
            entry.Level,
            entry.Service,
            entry.Message,
            entry.InstanceKey,
            entry.OperationTaskId,
            entry.CorrelationId,
            entry.TraceId,
            entry.Source,
            entry.Labels,
            entry.Fields);
}
