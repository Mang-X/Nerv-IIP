using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEvents;
using static Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters.SchedulingIntegrationEventConverterHelpers;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;

public sealed class SchedulePlanGeneratedIntegrationEventConverter
    : IIntegrationEventConverter<SchedulePlanGeneratedDomainEvent, SchedulingIntegrationEvent<SchedulePlanLifecyclePayload>>
{
    public SchedulingIntegrationEvent<SchedulePlanLifecyclePayload> Convert(SchedulePlanGeneratedDomainEvent domainEvent)
    {
        return PlanLifecycleEnvelope(
            SchedulingIntegrationEventTypes.SchedulePlanGenerated,
            "schedule-plan-generated",
            domainEvent.SchedulePlan);
    }
}

public sealed class ScheduleConflictDetectedIntegrationEventConverter
    : IIntegrationEventConverter<ScheduleConflictDetectedDomainEvent, SchedulingIntegrationEvent<ScheduleConflictDetectedPayload>>
{
    public SchedulingIntegrationEvent<ScheduleConflictDetectedPayload> Convert(ScheduleConflictDetectedDomainEvent domainEvent)
    {
        var plan = domainEvent.SchedulePlan;
        var conflict = domainEvent.Conflict;
        return Envelope(
            SchedulingIntegrationEventTypes.ScheduleConflictDetected,
            plan.OrganizationId,
            plan.EnvironmentId,
            EventIds.Idempotency("schedule-conflict-detected", plan.OrganizationId, plan.EnvironmentId, plan.PlanId, conflict.ConflictPublicId),
            new ScheduleConflictDetectedPayload(
                plan.PlanId,
                plan.ProblemId,
                plan.ContractVersion,
                plan.AlgorithmVersion,
                plan.ProblemFingerprint,
                Status(plan.Status),
                conflict.ConflictPublicId,
                EnumValue(conflict.ReasonCode),
                EnumValue(conflict.Severity),
                conflict.WorkOrderId,
                conflict.OperationId,
                conflict.ResourceId));
    }
}

public sealed class SchedulePlanReleasedIntegrationEventConverter
    : IIntegrationEventConverter<SchedulePlanReleasedDomainEvent, SchedulingIntegrationEvent<SchedulePlanLifecyclePayload>>
{
    public SchedulingIntegrationEvent<SchedulePlanLifecyclePayload> Convert(SchedulePlanReleasedDomainEvent domainEvent)
    {
        return PlanLifecycleEnvelope(
            SchedulingIntegrationEventTypes.SchedulePlanReleased,
            "schedule-plan-released",
            domainEvent.SchedulePlan);
    }
}

internal static class SchedulingIntegrationEventConverterHelpers
{
    public static SchedulingIntegrationEvent<SchedulePlanLifecyclePayload> PlanLifecycleEnvelope(
        string eventType,
        string idempotencyPrefix,
        SchedulePlan plan)
    {
        return Envelope(
            eventType,
            plan.OrganizationId,
            plan.EnvironmentId,
            EventIds.Idempotency(idempotencyPrefix, plan.OrganizationId, plan.EnvironmentId, plan.PlanId),
            new SchedulePlanLifecyclePayload(
                plan.PlanId,
                plan.ProblemId,
                plan.ContractVersion,
                plan.AlgorithmVersion,
                plan.ProblemFingerprint,
                Status(plan.Status),
                plan.Assignments
                    .OrderBy(x => x.StartUtc)
                    .ThenBy(x => x.WorkOrderId, StringComparer.Ordinal)
                    .ThenBy(x => x.OperationSequence)
                    .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                    .Select(x => new SchedulePlanAffectedOperationPayload(
                        x.WorkOrderId,
                        x.OperationId,
                        x.OperationSequence,
                        x.ResourceId,
                        x.WorkCenterId))
                    .ToArray()));
    }

    public static SchedulingIntegrationEvent<TPayload> Envelope<TPayload>(
        string eventType,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        TPayload payload)
    {
        return new SchedulingIntegrationEvent<TPayload>(
            EventIds.New(),
            eventType,
            1,
            DateTimeOffset.UtcNow,
            SchedulingIntegrationEventSources.BusinessScheduling,
            "system:scheduling",
            "system:scheduling",
            organizationId,
            environmentId,
            "system:scheduling",
            idempotencyKey,
            payload);
    }

    public static string Status(SchedulePlanLifecycleStatus status)
    {
        return status switch
        {
            SchedulePlanLifecycleStatus.Generated => "generated",
            SchedulePlanLifecycleStatus.Released => "released",
            _ => status.ToString()
        };
    }

    public static string EnumValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var text = value.ToString();
        return string.IsNullOrEmpty(text)
            ? text
            : char.ToLowerInvariant(text[0]) + text[1..];
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"scheduling:{string.Join(':', parts)}";
}
