using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Domain.DomainEvents;

public sealed record SchedulePlanGeneratedDomainEvent(SchedulePlan SchedulePlan) : IDomainEvent;

public sealed record ScheduleConflictDetectedDomainEvent(
    SchedulePlan SchedulePlan,
    SchedulePlanConflict Conflict) : IDomainEvent;

public sealed record SchedulePlanReleasedDomainEvent(SchedulePlan SchedulePlan) : IDomainEvent;

public sealed record SchedulePlanRevokedDomainEvent(SchedulePlan SchedulePlan) : IDomainEvent;

public sealed record SchedulePlanInvalidatedDomainEvent(
    SchedulePlanInvalidation Invalidation,
    SchedulePlanInvalidatedSnapshot Plan) : IDomainEvent;
