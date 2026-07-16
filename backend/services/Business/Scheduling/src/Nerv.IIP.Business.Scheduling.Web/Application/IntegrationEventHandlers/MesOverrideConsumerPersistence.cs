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
                SafeIdempotencyKey(envelope, IdempotencyKeyMaxLength),
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
            consumerName, originalEnvelope, failureCode,
            failureMessage.Length <= 1000 ? failureMessage : failureMessage[..1000]);
        return message with
        {
            EventId = SafeIdentity("event", originalEnvelope.EventId, DeadLetterEventIdMaxLength),
            EventType = SafeIdentity("type", originalEnvelope.EventType, DeadLetterEventTypeMaxLength),
            SourceService = SafeIdentity("source", originalEnvelope.SourceService, DeadLetterSourceServiceMaxLength),
            IdempotencyKey = SafeIdempotencyKey(originalEnvelope, DeadLetterIdempotencyKeyMaxLength)
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

        return HashIdentity(field, value ?? string.Empty);
    }

    private static string SafeIdempotencyKey(
        IIntegrationEventEnvelope envelope,
        int maxLength)
    {
        if (IsCanonical(envelope.IdempotencyKey, maxLength))
        {
            return envelope.IdempotencyKey;
        }

        var fingerprint = string.Join('|',
            FingerprintPart(envelope.IdempotencyKey),
            FingerprintPart(envelope.EventId),
            FingerprintPart(envelope.EventType),
            FingerprintPart(envelope.SourceService),
            FingerprintPart(envelope.CorrelationId),
            FingerprintPart(envelope.CausationId),
            FingerprintPart(envelope.OrganizationId),
            FingerprintPart(envelope.EnvironmentId),
            FingerprintPart(envelope.Actor),
            envelope.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            envelope.OccurredAtUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return HashIdentity("idempotency", fingerprint);
    }

    private static string FingerprintPart(string? value) =>
        $"{value?.Length ?? -1}:{value}";

    private static string HashIdentity(string field, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
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
