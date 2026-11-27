using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNetCore.CAP.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.Dto;
using StackExchange.Redis;
using EngineeringDb = Nerv.IIP.Business.ProductEngineering.Infrastructure.ApplicationDbContext;
using MesDb = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class ScheduledEngineeringChangeAcceptanceTests
{
    private const string Organization = "org-806";
    private const string EnvironmentId = "env-806";
    private const string ChangeNumber = "ECO-806";
    private const string WorkOrderId = "WO-806";
    private const string Token = "test-internal-token";
    private const string Actor = "user:operator-806";
    private const string Reason = "Complete the current run on the original version";
    private const string ContinueDecision = "continue-with-archived-version";
    private const string ChangePath = "/api/business/v1/engineering/engineering-changes";
    private const string DecisionPath = "/api/business/v1/mes/work-orders/" + WorkOrderId + "/engineering-change-decisions";
    private const string ImpactPath = "/api/business/v1/mes/work-order-engineering-change-impacts";
    private const string Scope = "organizationId=" + Organization + "&environmentId=" + EnvironmentId;
    private static readonly DateTimeOffset BeforeDue = new(2026, 7, 5, 23, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = BeforeDue.AddMinutes(1);
    private static readonly DateOnly EffectiveDate = DateOnly.FromDateTime(Due.UtcDateTime);
    private static readonly EventuallyOptions Observation = new(TimeSpan.FromSeconds(45), TimeSpan.FromMilliseconds(200), []);

    // #806 / #720 / #721: drive the scheduler's production scan by business date and observe the actual
    // MES read model through HTTP. Approval is pre-authorized fixture input.
    [ScheduledEcoRedisCapFact]
    [Trait("Lane", "redis-cap")]
    public async Task Future_eco_releases_at_due_scan_and_persists_scoped_work_order_decision_on_redis_cap()
    {
        var clock = new AcceptanceClock(BeforeDue);
        await using var mes = CreateFactory<ListEngineeringChangeWorkOrderImpactsEndpoint>(clock);
        using var mesClient = CreateClient(mes);
        await using var engineering = CreateFactory<ReleaseEngineeringChangeEndpoint>(clock);
        using var engineeringClient = CreateClient(engineering);
        var versionId = await SeedAsync(mes.Services, engineering.Services);
        await engineering.Services.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(RequiredEnvironment("NERV_IIP_TEST_REDIS"));
        var broker = redis.GetDatabase();
        var topic = $"{RequiredEnvironment("NERV_IIP_TEST_CAP_TOPIC_PREFIX")}.{EngineeringChangeReleasedIntegrationEventTopic.TopicName}";
        var consumerGroup = $"{EngineeringChangeReleasedIntegrationEventHandlerForMesWip.ConsumerName}.{RequiredEnvironment("NERV_IIP_TEST_CAP_VERSION")}";
        await Eventually.AssertAsync("the Redis ECO stream has the MES consumer group", async _ =>
        {
            Assert.True(await broker.KeyExistsAsync(topic));
            var groups = await broker.StreamGroupInfoAsync(topic);
            Assert.Contains(groups, group => group.Name == consumerGroup);
        }, Observation);

        using var release = await engineeringClient.PostAsJsonAsync($"{ChangePath}/release",
            new ReleaseEngineeringChangeRequest(Organization, EnvironmentId, ChangeNumber, "Retire previous version",
                "a8060000-1234-4234-8234-123456789abc", EffectiveDate, [new("production-version", versionId)]));
        await RequireSuccessAsync(release);

        Assert.Equal(0, await ScanAsync(engineering.Services));
        var scheduled = await ReadChangeAsync(engineeringClient);
        Assert.Equal("Scheduled", scheduled.Status);
        Assert.Equal(EffectiveDate, scheduled.EffectiveDate);
        Assert.Empty((await ReadImpactsAsync(mesClient, "WC-806")).Items);
        Assert.Equal((WorkOrder.StartedStatus, versionId), await ReadWorkOrderAsync(mes.Services));

        Assert.Equal(0L, await broker.StreamLengthAsync(topic));
        // The outbox check also catches a release that has not yet been sent to Redis.
        await using (var scope = engineering.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EngineeringDb>();
            Assert.Equal(ProductionVersionStatus.Active, await db.ProductionVersions.Select(x => x.Status).SingleAsync());
            Assert.Empty(await db.Database.SqlQuery<string>($"""
                SELECT "Name" AS "Value" FROM cap.published WHERE "Name" LIKE {"%." + EngineeringChangeReleasedIntegrationEventTopic.TopicName}
                """).ToArrayAsync());
        }

        clock.UtcNow = Due;
        Assert.Equal(1, await ScanAsync(engineering.Services));
        Assert.Equal("Published", (await ReadChangeAsync(engineeringClient)).Status);
        await Eventually.AssertAsync("MES exposes the released ECO impact in its work center",
            async ct =>
            {
                var impacts = await ReadImpactsAsync(mesClient, "WC-806", ct);
                Assert.Equal(1, impacts.Total);
                var item = Assert.Single(impacts.Items);
                Assert.Equal((ChangeNumber, WorkOrderId, "pending-decision"), (item.ChangeNumber, item.WorkOrderId, item.Disposition));
                await using var scope = mes.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MesDb>();
                Assert.Equal(["Succeeded"], await db.Database.SqlQuery<string>($"""
                    SELECT "StatusName" AS "Value" FROM cap.received
                    WHERE "Name" = {topic} AND "Group" = {consumerGroup}
                    """).ToArrayAsync(ct));
            }, Observation);
        Assert.Equal(1L, await broker.StreamLengthAsync(topic));

        Assert.Empty((await ReadImpactsAsync(mesClient, "WC-OTHER")).Items);
        Assert.Empty((await ReadImpactsAsync(mesClient, "WC-806", environmentId: "env-other")).Items);
        using var decision = await mesClient.PostAsJsonAsync(DecisionPath,
            new RecordEngineeringChangeDecisionRequest(Organization, EnvironmentId, WorkOrderId, ChangeNumber,
                ContinueDecision, Actor, Reason));
        await RequireSuccessAsync(decision);

        var history = Assert.Single((await ReadImpactsAsync(mesClient, "WC-806", identityFilter: $"workOrderId={WorkOrderId}")).Items);
        Assert.Equal((ChangeNumber, WorkOrderId, ContinueDecision), (history.ChangeNumber, history.WorkOrderId, history.Disposition));
        Assert.Equal(Actor, history.DecidedBy);
        Assert.Equal(Reason, history.DecisionReason);
        Assert.Equal(Due, history.DecidedAtUtc);
        Assert.Empty((await ReadImpactsAsync(mesClient, "WC-OTHER")).Items);
        Assert.Equal((WorkOrder.StartedStatus, versionId), await ReadWorkOrderAsync(mes.Services));
    }

    private static async Task<EngineeringChangeItem> ReadChangeAsync(HttpClient client)
    {
        var envelope = await client.GetFromJsonAsync<ResponseData<EngineeringChangeItem>>($"{ChangePath}/{ChangeNumber}?{Scope}");
        return Assert.IsType<EngineeringChangeItem>(envelope?.Data);
    }

    private static async Task<ListEngineeringChangeWorkOrderImpactsResponse> ReadImpactsAsync(
        HttpClient client, string workCenter, CancellationToken ct = default, string environmentId = EnvironmentId,
        string identityFilter = "changeNumber=" + ChangeNumber)
    {
        var response = await client.GetFromJsonAsync<ListEngineeringChangeWorkOrderImpactsResponse>(
            $"{ImpactPath}?organizationId={Organization}&environmentId={environmentId}&{identityFilter}&authorizedWorkCenterIds={workCenter}", ct);
        return Assert.IsType<ListEngineeringChangeWorkOrderImpactsResponse>(response);
    }

    private static async Task RequireSuccessAsync(HttpResponseMessage response) =>
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

    private static async Task<(string Status, string? VersionId)> ReadWorkOrderAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDb>();
        var row = await db.WorkOrders.AsNoTracking().SingleAsync();
        return (row.Status, row.ProductionVersionId);
    }

    private static async Task<int> ScanAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var date = scope.ServiceProvider.GetRequiredService<IProductEngineeringBusinessDateProvider>().GetBusinessDate();
        return await scope.ServiceProvider.GetRequiredService<EngineeringChangeScheduledReleaseService>()
            .PromoteDueReleasesAsync(date, CancellationToken.None);
    }

    private static async Task<string> SeedAsync(IServiceProvider mesServices, IServiceProvider engineeringServices)
    {
        await using var engineeringScope = engineeringServices.CreateAsyncScope();
        var engineering = engineeringScope.ServiceProvider.GetRequiredService<EngineeringDb>();
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(engineering);
        var version = ProductionVersion.Create(Organization, EnvironmentId, "FG-806", "MBOM:1", "ROUTE:1",
            EffectiveDate.AddDays(-30), null, null, null, 1, true, EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
        version.ClearDomainEvents();
        engineering.ProductionVersions.Add(version);
        await engineering.SaveChangesAsync();
        var versionId = version.Id.Id.ToString("D");

        await using var mesScope = mesServices.CreateAsyncScope();
        var mes = mesScope.ServiceProvider.GetRequiredService<MesDb>();
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(mes);
        var order = WorkOrder.Create(Organization, EnvironmentId, WorkOrderId, "FG-806", versionId, 10m, 1, Due.AddDays(1), "PCS");
        order.MarkReleased();
        order.Start(BeforeDue.AddHours(-1));
        order.ClearDomainEvents();
        mes.WorkOrders.Add(order);
        mes.OperationTasks.Add(OperationTask.Queue(Organization, EnvironmentId, WorkOrderId, "OP-10",
            10, "WC-806", [], BeforeDue, TimeSpan.FromHours(1), "FG-806"));
        await mes.SaveChangesAsync();
        return versionId;
    }

    private static HttpClient CreateClient<T>(WebApplicationFactory<T> factory) where T : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private static WebApplicationFactory<T> CreateFactory<T>(AcceptanceClock clock) where T : class =>
        new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            var redis = RequiredEnvironment("NERV_IIP_TEST_REDIS");
            var settings = new Dictionary<string, string?>
            {
                ["Persistence:AutoMigrate"] = "true",
                ["ConnectionStrings:PostgreSQL"] = AcceptancePostgresLaneDatabase.ConnectionString,
                ["ConnectionStrings:Redis"] = redis,
                ["Messaging:Provider"] = "Redis",
                ["Messaging:Redis:ConnectionString"] = redis,
                ["Cap:Version"] = RequiredEnvironment("NERV_IIP_TEST_CAP_VERSION"),
                ["Cap:TopicNamePrefix"] = RequiredEnvironment("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
                ["InternalService:BearerToken"] = Token,
                ["Walkthrough:Seed:Enabled"] = "false",
                ["ProductEngineering:EngineeringChangeRelease:Enabled"] = "false",
                ["ProductEngineering:EngineeringChangeRelease:TimeZoneId"] = "UTC",
            };
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(T).Assembly];
                    options.DisableAutoDiscovery = true;
                    options.IncludeAbstractValidators = typeof(T) == typeof(ReleaseEngineeringChangeEndpoint);
                });
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<IEngineeringApprovalVerifier>(new ApprovedFixture()));
            });
        });

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Missing {name}.");

    private sealed class AcceptanceClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ApprovedFixture : IEngineeringApprovalVerifier
    {
        public Task EnsureApprovedAsync(string organizationId, string environmentId, string approvalReferenceId,
            string changeNumber, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

internal sealed class ScheduledEcoRedisCapFactAttribute : FactAttribute
{
    public ScheduledEcoRedisCapFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NERV_IIP_TEST_DATABASE_LIFECYCLE") != "external")
        {
            Skip = "Requires the governed scheduled ECO Redis/CAP lane member and its isolated database.";
        }
    }
}
