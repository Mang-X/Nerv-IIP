using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

public sealed class IntegrationEventReliabilityTests
{
    [Fact]
    public async Task Consumer_guard_dead_letters_unsupported_event_version_without_invoking_handler()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = new IntegrationEventConsumerGuard<SampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(
                ConsumerName: "sample.consumer",
                ExpectedEventType: "sample.Event",
                SupportedEventVersion: 1));
        var invoked = false;

        await guard.HandleAsync(
            new SampleIntegrationEvent(
                EventId: "event-001",
                EventType: "sample.Event",
                EventVersion: 2,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                SourceService: "sample",
                CorrelationId: "corr-001",
                CausationId: "cause-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-001",
                Actor: "system:test",
                IdempotencyKey: "sample:event-001",
                Payload: new SamplePayload("value")),
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var messages = await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None);
        var message = Assert.Single(messages);
        Assert.False(invoked);
        Assert.Equal("unsupported-version", message.FailureCode);
        Assert.Equal("event-001", message.EventId);
        Assert.Equal("sample.Event", message.EventType);
        Assert.Equal(2, message.EventVersion);
    }

    [Fact]
    public async Task Consumer_guard_invokes_handler_and_skips_dead_letter_for_valid_envelope()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var guard = new IntegrationEventConsumerGuard<SampleIntegrationEvent>(
            new IntegrationEventEnvelopeValidator(),
            store,
            new IntegrationEventConsumerOptions(
                ConsumerName: "sample.consumer",
                ExpectedEventType: "sample.Event",
                SupportedEventVersion: 1));
        var handledEventIds = new List<string>();

        await guard.HandleAsync(
            CreateValidEvent("event-002"),
            (integrationEvent, _) =>
            {
                handledEventIds.Add(integrationEvent.EventId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(["event-002"], handledEventIds);
        Assert.Empty(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task Dead_letter_store_marks_pending_message_as_replayed()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var message = await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                "sample.consumer",
                CreateValidEvent("event-003"),
                "manual-replay-test",
                "Stored for replay."),
            CancellationToken.None);

        await store.MarkReplayedAsync(message.Id, DateTimeOffset.Parse("2026-05-25T00:00:00Z"), CancellationToken.None);

        var replayed = Assert.Single(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Replayed, CancellationToken.None));
        Assert.Equal(message.Id, replayed.Id);
        Assert.NotNull(replayed.ReplayedAtUtc);
        Assert.Empty(await store.ListAsync("sample.consumer", IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    private static SampleIntegrationEvent CreateValidEvent(string eventId)
    {
        return new SampleIntegrationEvent(
            EventId: eventId,
            EventType: "sample.Event",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            SourceService: "sample",
            CorrelationId: $"corr:{eventId}",
            CausationId: $"cause:{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:test",
            IdempotencyKey: $"sample:{eventId}",
            Payload: new SamplePayload("value"));
    }

    private sealed record SampleIntegrationEvent(
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
        SamplePayload Payload) : IIntegrationEventEnvelope
    {
        object? IIntegrationEventEnvelope.PayloadObject => Payload;
    }

    private sealed record SamplePayload(string Value);
}
