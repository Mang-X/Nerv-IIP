using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;

internal sealed record MesOverrideInboxIdentity(
    IIntegrationEventEnvelope Envelope,
    bool IsValid);

internal static class MesOverrideConsumerPersistence
{
    private const int EventIdMaxLength = 128;
    private const int EventTypeMaxLength = 256;
    private const int SourceServiceMaxLength = 128;
    private const int IdempotencyKeyMaxLength = 512;
    private const int DeadLetterEventIdMaxLength = 200;
    private const int DeadLetterEventTypeMaxLength = 300;
    private const int DeadLetterSourceServiceMaxLength = 150;
    private const int DeadLetterIdempotencyKeyMaxLength = 500;
    private const string OverrideUniqueIndexName =
        "IX_schedule_operation_overrides_organization_id_environment_id~";

    public static MesOverrideInboxIdentity CreateInboxIdentity(IIntegrationEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var eventIdValid = IsCanonical(envelope.EventId, EventIdMaxLength);
        var eventTypeValid = IsCanonical(envelope.EventType, EventTypeMaxLength);
        var sourceServiceValid = IsCanonical(envelope.SourceService, SourceServiceMaxLength);
        var idempotencyKeyValid = IsCanonical(envelope.IdempotencyKey, IdempotencyKeyMaxLength);

        return new MesOverrideInboxIdentity(
            new PersistableEnvelope(
                SafeIdentity("event", envelope.EventId, EventIdMaxLength),
                SafeIdentity("type", envelope.EventType, EventTypeMaxLength),
                envelope.EventVersion,
                envelope.OccurredAtUtc,
                SafeIdentity("source", envelope.SourceService, SourceServiceMaxLength),
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.OrganizationId,
                envelope.EnvironmentId,
                envelope.Actor,
                SafeIdentity("idempotency", envelope.IdempotencyKey, IdempotencyKeyMaxLength),
                envelope.PayloadObject),
            eventIdValid && eventTypeValid && sourceServiceValid && idempotencyKeyValid);
    }

    public static IntegrationEventDeadLetterMessage CreateDeadLetter(
        string consumerName,
        IIntegrationEventEnvelope originalEnvelope,
        string failureCode,
        string failureMessage)
    {
        var message = IntegrationEventDeadLetterMessage.Create(
            consumerName, originalEnvelope, failureCode, failureMessage);
        return message with
        {
            EventId = SafeIdentity("event", originalEnvelope.EventId, DeadLetterEventIdMaxLength),
            EventType = SafeIdentity("type", originalEnvelope.EventType, DeadLetterEventTypeMaxLength),
            SourceService = SafeIdentity("source", originalEnvelope.SourceService, DeadLetterSourceServiceMaxLength),
            IdempotencyKey = SafeIdentity(
                "idempotency", originalEnvelope.IdempotencyKey, DeadLetterIdempotencyKeyMaxLength)
        };
    }

    public static bool IsOverrideInsertConflict(
        DbUpdateException exception,
        ApplicationDbContext dbContext) =>
        dbContext.ChangeTracker.Entries<ScheduleOperationOverride>()
            .Any(entry => entry.State == EntityState.Added) &&
        ProcessedIntegrationEventInbox.IsUniqueConflict(
            exception, dbContext, OverrideUniqueIndexName);

    private static string SafeIdentity(string field, string? value, int maxLength)
    {
        if (IsCanonical(value, maxLength))
        {
            return value!;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return $"invalid-{field}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static bool IsCanonical(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() &&
        value.Length <= maxLength;

    private sealed record PersistableEnvelope(
        string EventId,
        string EventType,
        int EventVersion,
        DateTimeOffset OccurredAtUtc,
        string SourceService,
        string CorrelationId,
        string CausationId,
        string OrganizationId,
        string EnvironmentId,
        string Actor,
        string IdempotencyKey,
        object? PayloadObject) : IIntegrationEventEnvelope;
}
