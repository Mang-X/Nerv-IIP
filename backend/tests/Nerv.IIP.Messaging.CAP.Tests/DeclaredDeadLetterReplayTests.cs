using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IntegrationEvents;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

public sealed class DeclaredDeadLetterReplayTests
{
    [Fact]
    public async Task Batch_routes_original_consumer_and_distinguishes_rejection_exception_and_no_handler()
    {
        var store = new InMemoryIntegrationEventDeadLetterStore();
        var facts = new List<(string Consumer, SampleEvent Event)>();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventDeadLetterStore>(store);
        services.AddSingleton(facts);
        services.AddObservedIntegrationEventDeadLetterStore();
        Register("first");
        Register("second");
        using var provider = services.BuildServiceProvider();
        var replay = new IntegrationEventDeadLetterReplayExecutor(store,
            provider.GetServices<IIntegrationEventDeadLetterReplayHandler>(), TimeProvider.System);
        var success = await Add("second", "success");
        var reject = await Add("first", "reject");
        var exception = await Add("first", "exception");
        var noHandler = await Add("unknown", "unsupported");
        var incomplete = await Add("first", "incomplete");
        var results = await replay.ReplayBatchAsync(new(null, null, null), CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterReplayStatus.Replayed, results.Single(x => x.Id == success.Id).Status);
        foreach (var failed in new[] { reject, exception, incomplete })
        {
            Assert.Equal(IntegrationEventDeadLetterReplayStatus.Failed, results.Single(x => x.Id == failed.Id).Status);
            Assert.Equal(IntegrationEventDeadLetterStatus.Failed, (await store.GetAsync(failed.Id, CancellationToken.None))!.Status);
        }
        Assert.Equal(IntegrationEventDeadLetterReplayStatus.NoHandler, results.Single(x => x.Id == noHandler.Id).Status);
        Assert.Equal(noHandler, await store.GetAsync(noHandler.Id, CancellationToken.None));
        var fact = Assert.Single(facts);
        Assert.Equal("second", fact.Consumer);
        Assert.Equal(Event("success"), fact.Event);
        Assert.True((await replay.ReplayAsync(success.Id, CancellationToken.None)).Succeeded);
        Assert.Single(facts);
        Assert.Equal(success.EventJson, (await store.GetAsync(success.Id, CancellationToken.None))!.EventJson);

        void Register(string consumer) => services.AddIntegrationEventDeadLetterReplay<SampleEvent, Consumer>(
            consumer,
            (handler, integrationEvent, token) => handler.HandleAsync(consumer, integrationEvent, token),
            (_, integrationEvent, _) => Task.FromResult(integrationEvent.EventId == "reject" || facts.Any(x => x.Consumer == consumer && x.Event.EventId == integrationEvent.EventId)));
        Task<IntegrationEventDeadLetterMessage> Add(string consumer, string id) => store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(consumer, Event(id), "original", "original failure"), CancellationToken.None);
    }

    [Fact]
    public async Task Undeclared_event_type_does_not_modify_original_row()
    {
        var services = new ServiceCollection();
        var store = new InMemoryIntegrationEventDeadLetterStore();
        services.AddSingleton<IIntegrationEventDeadLetterStore>(store);
        services.AddSingleton(new List<(string Consumer, SampleEvent Event)>());
        services.AddObservedIntegrationEventDeadLetterStore();
        services.AddIntegrationEventDeadLetterReplay<SampleEvent, Consumer>("first", (h, e, t) => h.HandleAsync("first", e, t), (_, _, _) => Task.FromResult(true));
        using var provider = services.BuildServiceProvider();
        var original = await store.AddAsync(IntegrationEventDeadLetterMessage.Create("first", Event("unknown"), "original", "original failure")
            with { EventClrType = "undeclared.type" }, CancellationToken.None);
        var replay = new IntegrationEventDeadLetterReplayExecutor(store, provider.GetServices<IIntegrationEventDeadLetterReplayHandler>(), TimeProvider.System);
        Assert.Equal(IntegrationEventDeadLetterReplayStatus.NoHandler, (await replay.ReplayAsync(original.Id, CancellationToken.None)).Status);
        Assert.Equal(original, await store.GetAsync(original.Id, CancellationToken.None));
    }

    public sealed class Consumer(IIntegrationEventDeadLetterStore store, List<(string Consumer, SampleEvent Event)> facts)
    {
        public async Task HandleAsync(string consumer, SampleEvent integrationEvent, CancellationToken token)
        {
            if (integrationEvent.EventId == "exception") throw new InvalidOperationException("consumer exception");
            if (integrationEvent.EventId == "reject")
            {
                await store.AddAsync(IntegrationEventDeadLetterMessage.Create(consumer, integrationEvent, "business-rejected", "business refused"), token);
                return;
            }
            if (integrationEvent.EventId == "incomplete") return;
            if (!facts.Any(x => x.Consumer == consumer && x.Event.EventId == integrationEvent.EventId)) facts.Add((consumer, integrationEvent));
        }
    }

    public sealed record SampleEvent(string EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc,
        string SourceService, string CorrelationId, string CausationId, string OrganizationId, string EnvironmentId,
        string Actor, string IdempotencyKey, string Payload) : IIntegrationEventEnvelope
    {
        public object PayloadObject => Payload;
    }

    private static SampleEvent Event(string id) => new(id, "sample", 1, DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        "source", "correlation", "causation", "organization", "environment", "actor", id, "original payload");
}
