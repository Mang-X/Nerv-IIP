using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

[Collection(SchedulingPostgresLaneDatabase.CollectionName)]
public sealed class AssetUnavailableInboxPostgresProfileTests
{
    [SchedulingPostgresFact]
    public Task Concurrent_claims_with_same_event_id_and_different_business_keys_commit_one_result() =>
        RunRaceAsync("event-shared", "key-a", "event-shared", "key-b");

    [SchedulingPostgresFact]
    public Task Concurrent_claims_with_different_event_ids_and_same_business_key_commit_one_result() =>
        RunRaceAsync("event-a", "key-shared", "event-b", "key-shared");

    private static async Task RunRaceAsync(string firstEventId, string firstKey, string secondEventId, string secondKey)
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence(SchedulingPostgresLaneDatabase.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupDb.Database.MigrateAsync();
            var indexes = await setupDb.Database.SqlQueryRaw<string>("""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'scheduling'
                  AND tablename = 'processed_integration_events'
                  AND indexdef LIKE 'CREATE UNIQUE INDEX%'
                """).ToArrayAsync();
            Assert.Contains("ux_processed_integration_events_consumer_event_id", indexes);
            Assert.Contains("ux_processed_integration_events_consumer_idempotency_key", indexes);
        }

        var firstClaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CompeteAsync(provider, firstEventId, firstKey, "plan-a", null, firstClaimed, releaseFirst.Task);
        await firstClaimed.Task;
        var second = CompeteAsync(provider, secondEventId, secondKey, "plan-b", secondStarted, null, Task.CompletedTask);
        await secondStarted.Task;
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        Assert.Single(await db.SchedulePlanInvalidations.AsNoTracking().ToArrayAsync());
    }

    private static async Task CompeteAsync(
        ServiceProvider provider,
        string eventId,
        string idempotencyKey,
        string planId,
        TaskCompletionSource? started,
        TaskCompletionSource? claimed,
        Task release)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        started?.SetResult();
        var integrationEvent = Event(eventId, idempotencyKey);
        var won = await SchedulingProcessedIntegrationEventInbox.TryRecordAssetUnavailableAsync(
            db,
            AssetUnavailableIntegrationEventHandlerForInvalidateSchedulePlans.ConsumerName,
            integrationEvent,
            CancellationToken.None);
        claimed?.SetResult();
        if (won)
        {
            db.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
                "org-001", "env-dev", planId, eventId, integrationEvent.EventType,
                integrationEvent.SourceService, "equipmentUnavailable", "ASSET-CNC-01", null, null, null,
                integrationEvent.OccurredAtUtc, DateTimeOffset.UtcNow));
        }
        await release;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static AssetUnavailableV2IntegrationEvent Event(string eventId, string idempotencyKey) => new(
        eventId,
        MaintenanceIntegrationEventTypes.AssetUnavailable,
        MaintenanceIntegrationEventVersions.V2,
        DateTimeOffset.Parse("2026-06-01T09:00:00Z"),
        MaintenanceIntegrationEventSources.BusinessMaintenance,
        $"correlation-{eventId}",
        string.Empty,
        "org-001",
        "env-dev",
        "system:test",
        idempotencyKey,
        new AssetUnavailableV2Payload(
            "ASSET-CNC-01",
            "breakdown",
            DateTimeOffset.Parse("2026-06-01T09:00:00Z")));
}
