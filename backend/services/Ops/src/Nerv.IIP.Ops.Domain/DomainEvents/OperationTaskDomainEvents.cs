using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Ops.Domain.DomainEvents;

public sealed record OperationTaskCreatedDomainEvent(OperationTask OperationTask) : IDomainEvent;
public sealed record OperationTaskDispatchedDomainEvent(OperationTask OperationTask, OperationAttempt Attempt) : IDomainEvent;
public sealed record OperationResultRecordedDomainEvent(OperationTask OperationTask, OperationAttempt Attempt) : IDomainEvent;
