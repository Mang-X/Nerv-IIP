using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Contracts.Scheduling;
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
        var headerActor = ReadHeader(headers, "X-Actor");
        if (string.Equals(user?.FindFirstValue("token_type"), "internal_service", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(headerActor))
        {
            var trimmed = headerActor.Trim();
            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator >= trimmed.Length - 1 ||
                string.IsNullOrWhiteSpace(trimmed[..separator]) ||
                string.IsNullOrWhiteSpace(trimmed[(separator + 1)..]))
            {
                throw new KnownException("A canonical X-Actor is required for forwarded actor requests.");
            }

            return trimmed;
        }

        var subject = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
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
    : IIntegrationEventConverter<ScheduleConflictDetectedDomainEvent, ScheduleConflictDetectedIntegrationEvent>
{
    public ScheduleConflictDetectedIntegrationEvent Convert(ScheduleConflictDetectedDomainEvent domainEvent)
    {
        var plan = domainEvent.SchedulePlan;
        var conflict = domainEvent.Conflict;
        var context = contextAccessor.GetContext();
        var envelope = Envelope(
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
        return new ScheduleConflictDetectedIntegrationEvent(
            envelope.EventId,
            envelope.EventType,
            envelope.EventVersion,
            envelope.OccurredAtUtc,
            envelope.SourceService,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OrganizationId,
            envelope.EnvironmentId,
            envelope.Actor,
            envelope.IdempotencyKey,
            envelope.Payload);
    }
}

public sealed class SchedulePlanReleasedIntegrationEventConverter(
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SchedulePlanReleasedDomainEvent, SchedulePlanReleasedIntegrationEvent>
{
    public SchedulePlanReleasedIntegrationEvent Convert(SchedulePlanReleasedDomainEvent domainEvent)
    {
        var envelope = PlanLifecycleEnvelope(
            SchedulingIntegrationEventTypes.SchedulePlanReleased,
            "schedule-plan-released",
            domainEvent.SchedulePlan,
            timeProvider,
            contextAccessor.GetContext());
        return new SchedulePlanReleasedIntegrationEvent(
            envelope.EventId,
            envelope.EventType,
            envelope.EventVersion,
            envelope.OccurredAtUtc,
            envelope.SourceService,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OrganizationId,
            envelope.EnvironmentId,
            envelope.Actor,
            envelope.IdempotencyKey,
            envelope.Payload);
    }
}

public sealed class SchedulePlanRevokedIntegrationEventConverter(
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SchedulePlanRevokedDomainEvent, SchedulePlanRevokedIntegrationEvent>
{
    public SchedulePlanRevokedIntegrationEvent Convert(SchedulePlanRevokedDomainEvent domainEvent)
    {
        var plan = domainEvent.SchedulePlan;
        var releaseRevision = plan.ReleaseRevision
            ?? throw new InvalidOperationException("Revoked schedule plan must retain its release revision.");
        var revocationReason = plan.RevocationReason
            ?? throw new InvalidOperationException("Revoked schedule plan must retain its revocation reason.");
        var context = contextAccessor.GetContext();
        var envelope = Envelope(
            SchedulingIntegrationEventTypes.SchedulePlanRevoked,
            plan.OrganizationId,
            plan.EnvironmentId,
            EventIds.Idempotency(
                "schedule-plan-revoked",
                plan.OrganizationId,
                plan.EnvironmentId,
                plan.PlanId,
                releaseRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                EnumValue(revocationReason)),
            new SchedulePlanRevokedPayload(
                plan.PlanId,
                plan.ProblemId,
                plan.ContractVersion,
                plan.AlgorithmVersion,
                plan.ProblemFingerprint,
                releaseRevision,
                EnumValue(revocationReason),
                plan.SupersededByPlanId,
                AffectedOperations(plan)),
            timeProvider,
            context);
        return new SchedulePlanRevokedIntegrationEvent(
            envelope.EventId,
            envelope.EventType,
            envelope.EventVersion,
            envelope.OccurredAtUtc,
            envelope.SourceService,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OrganizationId,
            envelope.EnvironmentId,
            envelope.Actor,
            envelope.IdempotencyKey,
            envelope.Payload);
    }
}

public sealed class SchedulePlanInvalidatedIntegrationEventConverter(
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SchedulePlanInvalidatedDomainEvent, SchedulePlanInvalidatedIntegrationEvent>
{
    public SchedulePlanInvalidatedIntegrationEvent Convert(SchedulePlanInvalidatedDomainEvent domainEvent)
    {
        var invalidation = domainEvent.Invalidation;
        var plan = domainEvent.Plan;
        var affectedResourceIds = plan.AffectedOperations
            .SelectMany(x => new[] { x.ResourceId, x.WorkCenterId })
            .Append(invalidation.AffectedResourceId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var context = contextAccessor.GetContext();
        var envelope = Envelope(
            SchedulingIntegrationEventTypes.SchedulePlanInvalidated,
            invalidation.OrganizationId,
            invalidation.EnvironmentId,
            EventIds.Idempotency(
                "schedule-plan-invalidated",
                invalidation.OrganizationId,
                invalidation.EnvironmentId,
                invalidation.PlanId,
                invalidation.SourceEventId),
            new SchedulePlanInvalidatedPayload(
                plan.PlanId,
                plan.ProblemId,
                plan.ContractVersion,
                plan.AlgorithmVersion,
                plan.ProblemFingerprint,
                Status(plan.Status),
                invalidation.ReasonCode,
                invalidation.SourceEventType,
                invalidation.SourceEventId,
                affectedResourceIds,
                plan.AffectedOperations.Select(x => new SchedulePlanAffectedOperationPayload(
                    x.WorkOrderId,
                    x.OperationId,
                    x.OperationSequence,
                    x.ResourceId,
                    x.WorkCenterId,
                    x.StartUtc,
                    x.EndUtc,
                    x.StandardOperationCode)).ToArray()),
            timeProvider,
            context);
        return new SchedulePlanInvalidatedIntegrationEvent(
            envelope.EventId,
            envelope.EventType,
            envelope.EventVersion,
            envelope.OccurredAtUtc,
            envelope.SourceService,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OrganizationId,
            envelope.EnvironmentId,
            envelope.Actor,
            envelope.IdempotencyKey,
            envelope.Payload);
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
                AffectedOperations(plan),
                plan.ReleaseRevision),
            timeProvider,
            context);
    }

    public static SchedulePlanAffectedOperationPayload[] AffectedOperations(SchedulePlan plan)
    {
        return plan.Assignments
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .Select(x => new SchedulePlanAffectedOperationPayload(
                x.WorkOrderId,
                x.OperationId,
                x.OperationSequence,
                x.ResourceId,
                x.WorkCenterId,
                x.StartUtc,
                x.EndUtc,
                x.StandardOperationCode))
            .ToArray();
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
            SchedulePlanLifecycleStatus.Superseded => "superseded",
            SchedulePlanLifecycleStatus.Revoked => "revoked",
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
