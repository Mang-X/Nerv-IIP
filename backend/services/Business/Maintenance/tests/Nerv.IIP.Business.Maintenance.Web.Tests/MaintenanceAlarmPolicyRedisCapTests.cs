using System.Text.Json;
using DotNetCore.CAP;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

// #3256: real Redis/CAP input, PostgreSQL business/inbox/outbox results and redelivery after repair.
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceAlarmPolicyRedisCapTests
{
    private const string ExactCode = "Thermal_Custom-3256";

    [MaintenanceAlarmPolicyRedisCapFact]
    public async Task Alarm_policy_preserves_plain_creation_dual_facts_and_failed_message_retry_on_redis_cap()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            db.DowntimeReasons.Add(DowntimeReason.Create("org-a", "env-a", ExactCode, "Custom thermal reason"));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
        }

        var inputs = new[]
        {
            Alarm("plain-default", "org-unconfigured", "env-a", "Heat"),
            Alarm("plain-explicit", "org-plain", "env-a", "Heat"),
            Alarm("occupied", "org-a", "env-a", "Heat"),
            Alarm("case-miss", "org-a", "env-a", "heat"),
            Alarm("scope-miss", "org-a", "env-b", "Heat"),
        };
        foreach (var input in inputs) await PublishAsync(factory, input);
        await EventuallyAsync(factory, async (db, token) =>
        {
            Assert.Equal(5, await db.MaintenanceWorkOrders.CountAsync(token));
            Assert.Equal(5, await SucceededDeliveriesAsync(db, token));
        });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var orders = await db.MaintenanceWorkOrders.AsNoTracking().ToArrayAsync();
            foreach (var order in orders)
            {
                Assert.Equal(order.SourceAlarmId == "occupied", order.AssetUnavailable);
                Assert.Equal(order.SourceAlarmId == "occupied" ? ExactCode : null, order.AssetUnavailableReason);
                Assert.Equal("critical", order.Priority);
                Assert.Equal("industrialTelemetry", order.OpenedBy);
                Assert.Equal(order.SourceAlarmId == "case-miss" ? "heat" : "Heat", order.FailureModeCode);
                Assert.Equal("temperature", order.FailureCauseCode);
                Assert.Contains("96.5", order.DiagnosticDescription, StringComparison.Ordinal);
            }
            Assert.Equal(5, await db.CodeIdempotencyKeys.CountAsync());
            Assert.All(await db.CodeIdempotencyKeys.ToArrayAsync(), receipt => Assert.StartsWith("integration-event:", receipt.IdempotencyKey));
            await AssertUnavailableFactsAsync(db, 1);
        }

        // A new transport delivery with the same source alarm must not create another intent or fact.
        await PublishAsync(factory, inputs[2] with { EventId = "occupied-replay", IdempotencyKey = "occupied-replay" });
        await EventuallyAsync(factory, async (db, token) =>
        {
            Assert.Equal(6, await SucceededDeliveriesAsync(db, token));
            Assert.Equal(5, await db.MaintenanceWorkOrders.CountAsync(token));
            Assert.Equal(5, await db.CodeIdempotencyKeys.CountAsync(token));
            await AssertUnavailableFactsAsync(db, 1);
        });

        // Explicit occupancy with a missing same-scope code exhausts existing bounded CAP retries.
        var invalid = Alarm("missing-code", "org-invalid", "env-a", "Heat");
        await PublishAsync(factory, invalid);
        await EventuallyAsync(factory, async (db, token) =>
        {
            var failed = await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM cap.received WHERE "StatusName" = 'Failed' AND "Retries" >= 2""").SingleAsync(token);
            Assert.Equal(1, failed);
            Assert.False(await db.MaintenanceWorkOrders.AnyAsync(x => x.SourceAlarmId == invalid.Payload.ExternalAlarmId, token));
            Assert.False(await db.ProcessedIntegrationEvents.AnyAsync(x => x.EventId == invalid.EventId, token));
            Assert.Equal(5, await db.CodeIdempotencyKeys.CountAsync(token));
            await AssertUnavailableFactsAsync(db, 1);
        });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.DowntimeReasons.Add(DowntimeReason.Create("org-invalid", "env-a", ExactCode, "Repaired directory"));
            await db.SaveChangesAsync();
        }
        await PublishAsync(factory, invalid);
        await EventuallyAsync(factory, async (db, token) =>
        {
            Assert.Equal(7, await SucceededDeliveriesAsync(db, token));
            var repaired = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync(x => x.SourceAlarmId == "missing-code", token);
            Assert.True(repaired.AssetUnavailable);
            Assert.Equal(ExactCode, repaired.AssetUnavailableReason);
            Assert.Equal(6, await db.MaintenanceWorkOrders.CountAsync(token));
            Assert.Equal(6, await db.CodeIdempotencyKeys.CountAsync(token));
            Assert.True(await db.ProcessedIntegrationEvents.AnyAsync(x => x.EventId == invalid.EventId, token));
            await AssertUnavailableFactsAsync(db, 2);
        });
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"),
            ["Persistence:AutoMigrate"] = "false",
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
            ["IndustrialTelemetry:BaseUrl"] = "http://industrial-telemetry.local",
        };
        foreach (var (index, organization, mode, alarm) in new[]
        {
            (0, "org-a", "WorkOrderAndOccupy", "Heat"),
            (1, "org-plain", "WorkOrderOnly", (string?)null),
            (2, "org-invalid", "WorkOrderAndOccupy", (string?)null),
        })
        {
            var key = $"Maintenance:AlarmPolicy:Entries:{index}";
            settings[$"{key}:OrganizationId"] = organization;
            settings[$"{key}:EnvironmentId"] = "env-a";
            settings[$"{key}:Mode"] = mode;
            settings[$"{key}:AlarmCode"] = alarm;
            settings[$"{key}:AssetUnavailableReasonCode"] = mode == "WorkOrderOnly" ? null : ExactCode;
        }
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Issue3256");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services => services.PostConfigure<CapOptions>(options =>
            {
                options.SucceedMessageExpiredAfter = 3600;
                options.CollectorCleaningInterval = 3600;
                options.FailedRetryCount = 2;
                options.FailedRetryInterval = 1;
            }));
        });
    }

    private static Task<int> SucceededDeliveriesAsync(ApplicationDbContext db, CancellationToken token) =>
        db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM cap.received WHERE "StatusName" = 'Succeeded'""").SingleAsync(token);

    private static async Task AssertUnavailableFactsAsync(ApplicationDbContext db, int count)
    {
        var rows = await db.Database.SqlQueryRaw<string>("""SELECT "Content" AS "Value" FROM cap.published WHERE "Name" LIKE '%AssetUnavailableIntegrationEvent' OR "Name" LIKE '%asset-unavailable.v2'""").ToArrayAsync();
        Assert.Equal(count * 2, rows.Length);
        var values = rows.Select(row => Get(JsonDocument.Parse(row).RootElement, "value")).ToArray();
        Assert.Equal(count, values.Count(value => Get(value, "eventVersion").GetInt32() == 1));
        Assert.Equal(count, values.Count(value => Get(value, "eventVersion").GetInt32() == 2));
        foreach (var value in values)
        {
            var version = Get(value, "eventVersion").GetInt32();
            Assert.Equal(ExactCode, Get(Get(value, "payload"), version == 1 ? "reason" : "reasonCode").GetString());
        }
    }

    private static JsonElement Get(JsonElement value, string name) =>
        value.EnumerateObject().Single(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static async Task PublishAsync(WebApplicationFactory<Program> factory, AlarmRaisedIntegrationEvent alarm)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(nameof(AlarmRaisedIntegrationEvent), alarm);
    }

    private static ValueTask EventuallyAsync(WebApplicationFactory<Program> factory, Func<ApplicationDbContext, CancellationToken, Task> assertion) =>
        Eventually.AssertAsync("Maintenance alarm CAP delivery converges", async token =>
        {
            using var scope = factory.Services.CreateScope();
            await assertion(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), token);
        }, new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

    private static AlarmRaisedIntegrationEvent Alarm(string id, string organization, string environment, string code) => new(
        id, "industrialTelemetry.AlarmRaised", 1, DateTimeOffset.Parse("2026-09-01T00:00:00Z"), "industrialTelemetry",
        "corr-3256", id, organization, environment, "system:industrial-telemetry", id,
        new AlarmRaisedPayload(id, "device-3256", code, "critical", DateTimeOffset.Parse("2026-09-01T00:00:00Z"), id,
            null, "temperature", 96.5m, 90m, "celsius"));
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class MaintenanceAlarmPolicyRedisCapFactAttribute : FactAttribute
{
    public MaintenanceAlarmPolicyRedisCapFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP Maintenance alarm policy proof.";
    }
}
