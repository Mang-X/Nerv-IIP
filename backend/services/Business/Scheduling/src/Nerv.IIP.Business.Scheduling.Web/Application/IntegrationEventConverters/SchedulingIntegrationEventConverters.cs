using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEvents;
using static Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters.SchedulingIntegrationEventConverterHelpers;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;

public sealed record SchedulingIntegrationEventContext(
    string CorrelationId,
    string CausationId,
    string Actor);

public interface ISchedulingIntegrationEventContextAccessor
{
    SchedulingIntegrationEventContext GetContext();
}

public sealed class HttpSchedulingIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ISchedulingIntegrationEventContextAccessor
{
    public SchedulingIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var headers = httpContext?.Request.Headers;

        return new SchedulingIntegrationEventContext(
            ReadHeader(headers, "X-Correlation-Id")
                ?? Activity.Current?.GetTagItem("correlationId")?.ToString()
                ?? Guid.NewGuid().ToString("n"),
            ReadHeader(headers, "X-Causation-Id") ?? Guid.NewGuid().ToString("n"),
            ResolveActor(httpContext?.User, headers));
    }

    private static string ResolveActor(ClaimsPrincipal? user, IHeaderDictionary? headers)
    {
        var subject = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        var headerActor = ReadHeader(headers, "X-Actor");
        if (!string.IsNullOrWhiteSpace(headerActor))
        {
            return headerActor;
        }

        var name = user?.Identity?.Name;
        return string.IsNullOrWhiteSpace(name)
            ? $"system:{SchedulingIntegrationEventSources.BusinessScheduling}"
            : $"user:{name}";
    }

    private static string? ReadHeader(IHeaderDictionary? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out StringValues values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

public sealed class SchedulePlanGeneratedIntegrationEventConverter(
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SchedulePlanGeneratedDomainEvent, SchedulingIntegrationEvent<SchedulePlanLifecyclePayload>>
{
    public SchedulingIntegrationEvent<SchedulePlanLifecyclePayload> Convert(SchedulePlanGeneratedDomainEvent domainEvent)
    {
        return PlanLifecycleEnvelope(
            SchedulingIntegrationEventTypes.SchedulePlanGenerated,
            "schedule-plan-generated",
            domainEvent.SchedulePlan,
            timeProvider,
            contextAccessor.GetContext());
    }
}

public sealed class ScheduleConflictDetectedIntegrationEventConverter(
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<ScheduleConflictDetectedDomainEvent, SchedulingIntegrationEvent<ScheduleConflictDetectedPayload>>
{
    public SchedulingIntegrationEvent<ScheduleConflictDetectedPayload> Convert(ScheduleConflictDetectedDomainEvent domainEvent)
    {
        var plan = domainEvent.SchedulePlan;
        var conflict = domainEvent.Conflict;
        var context = contextAccessor.GetContext();
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
                conflict.ResourceId),
            timeProvider,
            context);
    }
}

public sealed class SchedulePlanReleasedIntegrationEventConverter(
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SchedulePlanReleasedDomainEvent, SchedulingIntegrationEvent<SchedulePlanLifecyclePayload>>
{
    public SchedulingIntegrationEvent<SchedulePlanLifecyclePayload> Convert(SchedulePlanReleasedDomainEvent domainEvent)
    {
        return PlanLifecycleEnvelope(
            SchedulingIntegrationEventTypes.SchedulePlanReleased,
            "schedule-plan-released",
            domainEvent.SchedulePlan,
            timeProvider,
            contextAccessor.GetContext());
    }
}

internal static class SchedulingIntegrationEventConverterHelpers
{
    public static SchedulingIntegrationEvent<SchedulePlanLifecyclePayload> PlanLifecycleEnvelope(
        string eventType,
        string idempotencyPrefix,
        SchedulePlan plan,
        TimeProvider timeProvider,
        SchedulingIntegrationEventContext context)
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
                    .ToArray()),
            timeProvider,
            context);
    }

    public static SchedulingIntegrationEvent<TPayload> Envelope<TPayload>(
        string eventType,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        TPayload payload,
        TimeProvider timeProvider,
        SchedulingIntegrationEventContext context)
    {
        return new SchedulingIntegrationEvent<TPayload>(
            EventIds.New(),
            eventType,
            1,
            timeProvider.GetUtcNow(),
            SchedulingIntegrationEventSources.BusinessScheduling,
            context.CorrelationId,
            context.CausationId,
            organizationId,
            environmentId,
            context.Actor,
            idempotencyKey,
            payload);
    }

    public static string Status(SchedulePlanLifecycleStatus status)
    {
        return status switch
        {
            SchedulePlanLifecycleStatus.Generated => "generated",
            SchedulePlanLifecycleStatus.Released => "released",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown schedule plan lifecycle status.")
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
