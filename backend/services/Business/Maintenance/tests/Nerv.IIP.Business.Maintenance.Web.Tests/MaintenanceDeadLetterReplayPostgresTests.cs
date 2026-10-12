using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNetCore.CAP;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

// #4355 / 父 spec r1：以生产 Program、消费者和 PostgreSQL 事实证明原服务恢复。
// CAP 使用生产 EF outbox + InMemory transport；不声称下游跨进程恢复。
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceDeadLetterReplayPostgresTests
{
    private static readonly DateTimeOffset OriginalTime = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private const string Route = "/api/business/v1/maintenance/dlq";

    [ReplayPostgresFact]
    public async Task Production_http_replay_recovers_alarm_clear_and_device_plans_without_duplicate_work_orders()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_maintenance_replay");
        try
        {
            await using var factory = CreateFactory(database.ConnectionString);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-replay-token");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(default);
                await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(default);
                db.MaintenancePlans.AddRange(
                    Plan("org-replay", "env-replay", "DEV-REPLAY", "match"),
                    Plan("other-org", "env-replay", "DEV-REPLAY", "other-org"),
                    Plan("org-replay", "other-env", "DEV-REPLAY", "other-env"),
                    Plan("org-replay", "env-replay", "OTHER-DEVICE", "other-device"));
                await db.SaveChangesAsync();
            }

            var raised = Raised("raised-original", "ALARM-ORIGINAL");
            var letter = await Add(raised, OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName);
            await Replay(letter, "replayed");
            await Replay(letter, "replayed");
            // 新 eventId / inbox key 的重复报警也只能绑定同一原报警工单。
            var duplicate = Raised("raised-duplicate", "ALARM-ORIGINAL");
            await Replay(await Add(duplicate, OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName), "replayed");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var order = Assert.Single(await db.MaintenanceWorkOrders.AsNoTracking().ToListAsync());
                Assert.Equal(("org-replay", "env-replay", "DEV-REPLAY", "ALARM-ORIGINAL"),
                    (order.OrganizationId, order.EnvironmentId, order.DeviceAssetId, order.SourceAlarmId));
                Assert.Equal("OVER_TEMP", order.FailureModeCode);
                Assert.False(order.AlarmCleared);
                Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
                Assert.Equal(2, await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM cap.published").SingleAsync());
            }

            var cleared = new AlarmClearedIntegrationEvent("clear-original", "industrialTelemetry.AlarmCleared", 1,
                OriginalTime.AddHours(1), "industrialTelemetry", "original-correlation", "original-causation",
                "org-replay", "env-replay", "system:test", "clear-original",
                new AlarmClearedPayload("alarm-event", "DEV-REPLAY", "OVER_TEMP", "critical", OriginalTime,
                    OriginalTime.AddHours(1), "ALARM-ORIGINAL"));
            var disabled = new DeviceAssetChangedIntegrationEvent("device-original", MasterDataIntegrationEventTypes.DeviceAssetChanged, 1,
                OriginalTime, MasterDataIntegrationEventSources.BusinessMasterData, "original-correlation", "original-causation",
                "org-replay", "env-replay", "system:test", "device-original",
                new MasterDataChangedPayload("device-asset", "DEV-REPLAY", "disabled", OriginalTime));
            var clearLetter = await Add(cleared, MarkWorkOrderAlarmClearedHandler.ConsumerName);
            var deviceLetter = await Add(disabled, PauseMaintenancePlansWhenDeviceDisabledHandler.ConsumerName);
            var unsupported = await Add(raised, "unregistered-consumer");
            using var batch = await client.PostAsJsonAsync(Route + "/replay-batch", new { status = "pending" });
            Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
            using var batchJson = JsonDocument.Parse(await batch.Content.ReadAsStringAsync());
            var results = batchJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(3, results.Length);
            Assert.Equal(2, results.Count(x => x.GetProperty("succeeded").GetBoolean()));
            Assert.Single(results, x => x.GetProperty("status").GetString() == "noHandler");
            await Replay(clearLetter, "replayed");
            await Replay(deviceLetter, "replayed");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var order = Assert.Single(await db.MaintenanceWorkOrders.AsNoTracking().ToListAsync());
                Assert.True(order.AlarmCleared);
                Assert.Equal(OriginalTime.AddHours(1), order.AlarmClearedAtUtc);
                var plans = await db.MaintenancePlans.AsNoTracking().ToListAsync();
                Assert.True(plans.Single(x => x.PlanCode == "match").Paused);
                Assert.All(plans.Where(x => x.PlanCode != "match"), x => Assert.False(x.Paused));
                var state = Assert.Single(await db.MaintenanceDeviceStates.AsNoTracking().ToListAsync());
                Assert.True(state.Disabled);
                Assert.Equal(disabled.EventId, state.SourceEventId);
                Assert.Equal(OriginalTime, state.ChangedAtUtc);
                var inbox = await db.ProcessedIntegrationEvents.AsNoTracking().ToListAsync();
                Assert.Equal(4, inbox.Count);
                Assert.Contains(inbox, x => x.EventId == raised.EventId && x.IdempotencyKey == raised.IdempotencyKey);
                Assert.Equal(2, await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM cap.published").SingleAsync());
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                Assert.Equal(unsupported, await store.GetAsync(unsupported.Id, default));
                foreach (var original in new[] { letter, clearLetter, deviceLetter })
                {
                    var readback = await store.GetAsync(original.Id, default);
                    Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, readback!.Status);
                    Assert.Equal(original.EventJson, readback.EventJson);
                    Assert.Equal(original.EventId, readback.EventId);
                }
            }

            async Task<IntegrationEventDeadLetterMessage> Add(IIntegrationEventEnvelope integrationEvent, string consumer)
            {
                using var scope = factory.Services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
                var added = await store.AddAsync(
                    IntegrationEventDeadLetterMessage.Create(consumer, integrationEvent, "original-failure", "simulated original outage"), default);
                return (await store.GetAsync(added.Id, default))!;
            }
            async Task Replay(IntegrationEventDeadLetterMessage original, string expected)
            {
                using (var readScope = factory.Services.CreateScope())
                    original = (await readScope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(original.Id, default))!;
                using var response = await client.PostAsync($"{Route}/{original.Id}/replay", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("status").GetString());
            }
        }
        finally { await database.DropAsync(); }
    }

    [ReplayPostgresFact]
    public async Task Guard_rejection_and_command_failure_do_not_complete_or_leave_inbox_facts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_maintenance_replay");
        try
        {
            await using var factory = CreateFactory(database.ConnectionString);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-replay-token");
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            foreach (var integrationEvent in new[] {
                Raised("bad-version", "BAD-VERSION") with { EventVersion = 99 },
                Raised("bad-command", "BAD-COMMAND") with { Payload = Raised("unused", "BAD-COMMAND").Payload with { DeviceAssetId = new string('x', 151) } } })
            {
                IntegrationEventDeadLetterMessage original;
                using (var scope = factory.Services.CreateScope())
                    original = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                        IntegrationEventDeadLetterMessage.Create(OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName, integrationEvent, "original", "original failure"), default);
                using (var readScope = factory.Services.CreateScope())
                    original = (await readScope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(original.Id, default))!;
                using var response = await client.PostAsync($"{Route}/{original.Id}/replay", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("failed", json.RootElement.GetProperty("data").GetProperty("status").GetString());
                using var scopeAfter = factory.Services.CreateScope();
                var db = scopeAfter.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Empty(await db.MaintenanceWorkOrders.ToListAsync());
                Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
                var failed = await scopeAfter.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(original.Id, default);
                Assert.Equal(IntegrationEventDeadLetterStatus.Failed, failed!.Status);
                Assert.Equal(original.EventJson, failed.EventJson);
            }
        }
        finally { await database.DropAsync(); }
    }

    private static MaintenancePlan Plan(string org, string env, string device, string code) =>
        MaintenancePlan.Create(org, env, device, code, "P7D", new DateOnly(2026, 10, 1), "maintenance");
    private static AlarmRaisedIntegrationEvent Raised(string id, string alarm) => new(id, "industrialTelemetry.AlarmRaised", 1,
        OriginalTime, "industrialTelemetry", "original-correlation", "original-causation", "org-replay", "env-replay", "system:test", id,
        new AlarmRaisedPayload("alarm-event", "DEV-REPLAY", "OVER_TEMP", "critical", OriginalTime, alarm, "p1"));
    private static WebApplicationFactory<Program> CreateFactory(string connection) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string?> {
                ["ConnectionStrings:PostgreSQL"] = connection, ["Persistence:AutoMigrate"] = "false",
                ["Maintenance:Seed:Enabled"] = "false", ["InternalService:BearerToken"] = "test-replay-token",
                ["Messaging:Provider"] = "InMemory", ["Cap:Version"] = "issue4355",
                ["FastEndpoints:RestrictDiscoveryToEntryAssembly"] = "true",
            };
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
    private sealed class ReplayPostgresFactAttribute : FactAttribute
    {
        public ReplayPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
                Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL Maintenance dead-letter replay tests.";
        }
    }
}
