namespace Nerv.IIP.Ops.Domain;

public sealed record OperationTaskBoundary(string OperationTaskId, string InstanceKey, string OperationCode, string Status);
public sealed record AuditRecordBoundary(string AuditRecordId, string OperationTaskId, string Actor, DateTimeOffset OccurredAtUtc);
