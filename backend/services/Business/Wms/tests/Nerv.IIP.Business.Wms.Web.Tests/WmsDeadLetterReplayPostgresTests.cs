using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Web.Application.WcsAdapters;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WmsDeadLetterReplayPostgresTests
{
    [WmsDeadLetterReplayPostgresFact]
    public async Task Quality_rejection_does_not_commit_inbox_and_recovers_original_event_through_http_on_postgres()
    {
        await using var fixture = await ReplayFixture.CreateAsync();
        var ev = Inspection("quality-new", "IN-NEW");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate>()
                .HandleAsync(ev, default);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.ProcessedIntegrationEvents.ToListAsync());
        }
        var letter = await fixture.LetterAsync(ev, QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName);
        await fixture.ReplayAsync(letter, "failed");
        await fixture.SeedAsync(db => db.InboundOrders.Add(ReceivedInbound("IN-NEW")));
        await fixture.ReplayAsync(letter, "replayed");
        await fixture.ReplayAsync(letter, "replayed");
        using var read = fixture.Factory.Services.CreateScope();
        var result = await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().InboundOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(InboundOrderStatus.Completed, result.Status);
        Assert.Equal("passed", Assert.Single(result.Lines).QualityGateStatus);
        Assert.Equal("QI-quality-new", Assert.Single(result.Lines).InspectionRecordId);
        Assert.Single(await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(letter.EventJson, (await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(letter.Id, default))!.EventJson);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Legacy_failed_inbox_recovers_only_matching_unfinished_quality_fact_and_preserves_existing_effect_on_postgres()
    {
        await using var fixture = await ReplayFixture.CreateAsync();
        var ev = Inspection("quality-legacy", "IN-LEGACY");
        await fixture.SeedAsync(db =>
        {
            db.InboundOrders.Add(ReceivedInbound("IN-LEGACY"));
            db.ProcessedIntegrationEvents.Add(Inbox(ev));
            db.ProcessedIntegrationEvents.Add(Inbox(ev with { EventId = "unrelated", IdempotencyKey = "unrelated" }));
        });
        var letter = await fixture.AddLetterAsync(ev, QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName);
        await fixture.ReplayAsync(letter, "replayed");
        await fixture.ReplayAsync(letter, "replayed");
        await fixture.ReplayAsync(await fixture.AddLetterAsync(ev with { OrganizationId = "other-org" }, letter.ConsumerName), "failed");
        await fixture.ReplayAsync(await fixture.AddLetterAsync(ev with { EnvironmentId = "other-env" }, letter.ConsumerName), "failed");
        var divergence = ev with { EventId = "divergence", IdempotencyKey = "divergence", Payload = ev.Payload with { InspectedQuantity = 6m } };
        await fixture.ReplayAsync(await fixture.AddLetterAsync(divergence, letter.ConsumerName), "failed");
        // A different inspection cannot use an existing inbox to overwrite a completed quality result.
        var conflict = ev with { EventId = "quality-conflict", IdempotencyKey = "conflict", Payload = ev.Payload with { InspectionRecordId = "QI-CONFLICT" } };
        await fixture.SeedAsync(db => db.ProcessedIntegrationEvents.Add(Inbox(conflict)));
        await fixture.ReplayAsync(await fixture.AddLetterAsync(conflict, letter.ConsumerName), "failed");
        // A different event instance sharing the original key cannot borrow its completion fact.
        await fixture.ReplayAsync(await fixture.AddLetterAsync(ev with { EventId = "different-event" }, letter.ConsumerName), "failed");
        using var read = fixture.Factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var line = Assert.Single((await db.InboundOrders.Include(x => x.Lines).SingleAsync()).Lines);
        Assert.Equal("QI-quality-legacy", line.InspectionRecordId);
        Assert.Equal("passed", line.QualityGateStatus);
        Assert.Equal(3, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.True(await db.ProcessedIntegrationEvents.AnyAsync(x => x.EventId == "unrelated"));
    }

    internal static InspectionResultIntegrationEvent Inspection(string id, string inbound) => new(id,
        QualityIntegrationEventTypes.InspectionPassed, 1, DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        QualityIntegrationEventSources.BusinessQuality, "original-correlation", "original-causation", "org-replay", "env-replay", "system:quality", id,
        new InspectionResultPayload("QI-" + id, "PLAN", "receiving", "wms", inbound, "SKU", 5m, "passed", null, [],
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"), new StockReleaseDimensionPayload("kg", "SITE", "STAGE", "LOT", null, "quality", "company", null)));

    internal static InboundOrder ReceivedInbound(string number)
    {
        var inbound = InboundOrder.Create("org-replay", "env-replay", number, "purchase-receipt", "PO", "SITE",
            [new InboundOrderLineDraft("1", "SKU", "kg", 5m, "STAGE", "LOT", null, "quality", "company", null)]);
        inbound.Complete("receive-" + number, inbound.Version);
        return inbound;
    }

    private static ProcessedIntegrationEvent Inbox(InspectionResultIntegrationEvent ev) => new(
        QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName, ev.EventId, ev.EventType,
        ev.EventVersion, ev.SourceService, ev.IdempotencyKey, ev.OccurredAtUtc);
}

internal sealed class ReplayFixture(PostgreSqlTestDatabase database, WebApplicationFactory<Program> factory, HttpClient client) : IAsyncDisposable
{
    public WebApplicationFactory<Program> Factory => factory;
    public HttpClient Client => client;

    public static async Task<ReplayFixture> CreateAsync(Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var database = await PostgreSqlTestDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_wms_deadletter");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:PostgreSQL", database.ConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                ["ConnectionStrings:PostgreSQL"] = database.ConnectionString,
                ["InternalService:BearerToken"] = "test-replay-token",
                    ["Persistence:AutoMigrate"] = "false"
                });
                if (settings is not null) configuration.AddInMemoryCollection(settings);
            });
            if (configure is not null) builder.ConfigureServices(configure);
        });
        try
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-replay-token");
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            await db.Database.MigrateAsync();
            return new ReplayFixture(database, factory, client);
        }
        catch
        {
            try { await factory.DisposeAsync(); }
            finally
            {
                try { await database.DropAsync(); }
                finally { await database.DisposeAsync(); }
            }
            throw;
        }
    }

    public async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<IntegrationEventDeadLetterMessage> AddLetterAsync<T>(T ev, string consumer) where T : IIntegrationEventEnvelope
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var letter = await store.AddAsync(IntegrationEventDeadLetterMessage.Create(consumer, ev, "original", "original failure"), default);
        return (await store.GetAsync(letter.Id, default))!;
    }

    public async Task<IntegrationEventDeadLetterMessage> LetterAsync(IIntegrationEventEnvelope ev, string consumer)
    {
        using var scope = Factory.Services.CreateScope();
        return Assert.Single(await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().ListAsync(consumer, null, default),
            x => x.EventId == ev.EventId);
    }

    public async Task ReplayAsync(IntegrationEventDeadLetterMessage letter, string expected)
    {
        using var response = await Client.PostAsync($"/api/business/v1/wms/dlq/{letter.Id}/replay", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("data");
        Assert.True(expected == result.GetProperty("status").GetString(), result.ToString());
        using var scope = Factory.Services.CreateScope();
        var read = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(letter.Id, default);
        Assert.Equal(expected == "replayed" ? IntegrationEventDeadLetterStatus.Replayed : IntegrationEventDeadLetterStatus.Failed, read!.Status);
        Assert.Equal(letter.EventJson, read.EventJson);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        try { await factory.DisposeAsync(); }
        finally
        {
            try { await database.DropAsync(); }
            finally { await database.DisposeAsync(); }
        }
    }
}

public sealed class WmsDeadLetterReplayPostgresFactAttribute : FactAttribute
{
    public WmsDeadLetterReplayPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
            Skip = "Set NERV_IIP_TEST_POSTGRES to run WMS dead-letter HTTP recovery tests.";
    }
}
