using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Ops.Domain.DomainEvents;

public sealed record OperationTaskCreatedDomainEvent(OperationTask OperationTask) : IDomainEvent;
public sealed record OperationApprovalRequestedDomainEvent(OperationTask OperationTask) : IDomainEvent;
public sealed record OperationTaskDispatchedDomainEvent(OperationTask OperationTask, OperationAttempt Attempt) : IDomainEvent;
public sealed record AuditRecordedDomainEvent(OperationTask OperationTask, AuditRecord AuditRecord) : IDomainEvent;
public sealed record OperationResultRecordedDomainEvent(OperationTask OperationTask, OperationAttempt Attempt) : IDomainEvent;
public sealed record OperationTaskCompletedDomainEvent(OperationTask OperationTask, OperationAttempt Attempt, OperationResult Result) : IDomainEvent;
public sealed record OperationTaskFailedDomainEvent(OperationTask OperationTask, OperationAttempt Attempt, OperationResult Result) : IDomainEvent;
public sealed record OperationTaskApprovedDomainEvent(OperationTask OperationTask, string ApprovedBy, string DecisionReason, string CorrelationId, DateTimeOffset ApprovedAtUtc) : IDomainEvent;
public sealed record OperationTaskRejectedDomainEvent(OperationTask OperationTask, string RejectedBy, string DecisionReason, string CorrelationId, DateTimeOffset RejectedAtUtc) : IDomainEvent;
