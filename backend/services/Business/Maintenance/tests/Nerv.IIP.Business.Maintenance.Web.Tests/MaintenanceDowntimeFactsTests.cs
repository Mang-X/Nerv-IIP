using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

// PublicContract / #4245 and approved #3629 spec r1: raw start and actual release are not forecasts.
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceDowntimeFactsTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static object Request => new
    {
        organizationId = "org", environmentId = "env", deviceAssetIds = new[] { "device" },
        windowStartUtc = From.AddHours(1), windowEndUtc = DateTimeOffset.MaxValue,
    };

    [Fact]
    public async Task Public_query_preserves_raw_start_release_scope_and_ETR_update_clear()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var active = Order("org", "env", "device");
        var completed = Order("org", "env", "device");
        completed.Complete("fixed", "fault", 20, []);
        var cancelled = Order("org", "env", "device");
        cancelled.Cancel();
        var clearedAlarm = MaintenanceWorkOrder.OpenFromAlarm("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "alarm", "high", sourceReferenceId: "MWO-FACT-001");
        clearedAlarm.MarkAssetUnavailable(From, "fault");
        clearedAlarm.MarkAlarmCleared(From.AddMinutes(30));
        db.MaintenanceWorkOrders.AddRange(active, completed, cancelled, clearedAlarm,
            Order("other-org", "env", "device"), Order("org", "other-env", "device"), Order("org", "env", "other-device"));
        await db.SaveChangesAsync();
        await using var factory = Factory(db);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var items = await Read(client);
        Assert.Equal(4, items.Length);
        var fact = items.Single(x => x.GetProperty("workOrderId").GetString() == active.Id.ToString());
        Assert.Equal(From, fact.GetProperty("unavailableFromUtc").GetDateTimeOffset());
        Assert.Equal("device", fact.GetProperty("deviceAssetId").GetString());
        Assert.Equal("maintenance", fact.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, fact.GetProperty("releasedAtUtc").ValueKind);
        Assert.Equal(From.AddHours(2), fact.GetProperty("expectedRestoreAtUtc").GetDateTimeOffset());
        Assert.Equal(completed.CompletedAtUtc, items.Single(x => x.GetProperty("workOrderId").GetString() == completed.Id.ToString()).GetProperty("releasedAtUtc").GetDateTimeOffset());
        Assert.Equal(cancelled.CancelledAtUtc, items.Single(x => x.GetProperty("workOrderId").GetString() == cancelled.Id.ToString()).GetProperty("releasedAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, items.Single(x => x.GetProperty("workOrderId").GetString() == clearedAlarm.Id.ToString()).GetProperty("releasedAtUtc").ValueKind);
        Assert.Equal("MWO-FACT-001", items.Single(x => x.GetProperty("workOrderId").GetString() == clearedAlarm.Id.ToString()).GetProperty("sourceReferenceId").GetString());
        active.UpdateExpectedRestore(From.AddHours(3));
        await db.SaveChangesAsync();
        fact = (await Read(client)).Single(x => x.GetProperty("workOrderId").GetString() == active.Id.ToString());
        Assert.Equal(From.AddHours(3), fact.GetProperty("expectedRestoreAtUtc").GetDateTimeOffset());
        active.UpdateExpectedRestore(null);
        await db.SaveChangesAsync();
        fact = (await Read(client)).Single(x => x.GetProperty("workOrderId").GetString() == active.Id.ToString());
        Assert.Equal(JsonValueKind.Null, fact.GetProperty("expectedRestoreAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, fact.GetProperty("releasedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Service_query_filters_by_actual_overlap_and_reuses_the_prediction_owner()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var active = Order("org", "env", "device");
        active.UpdateExpectedRestore(null);
        var ended = Order("org", "env", "device");
        ended.Cancel();
        var future = MaintenanceWorkOrder.OpenManual("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "high", "operator");
        future.MarkAssetUnavailable(ended.CancelledAtUtc!.Value.AddHours(1), "fault");
        var notUnavailable = MaintenanceWorkOrder.OpenManual("org", "env", $"MWO-T-{Guid.NewGuid():N}", "device", "high", "operator");
        db.MaintenanceWorkOrders.AddRange(active, ended, future, notUnavailable);
        await db.SaveChangesAsync();
        var owner = new GetMaintenanceRestorePredictionQueryHandler(db, Options.Create(new MaintenanceRestorePredictionOptions()));
        var expected = await owner.Handle(new("org", "env", active.Id), default);
        var query = new QueryMaintenanceDowntimeFactsQuery(new MaintenanceDowntimeFactsRequest(
            "org", "env", ended.CancelledAtUtc!.Value, future.AssetUnavailableFromUtc!.Value, ["device"]));
        var response = await new QueryMaintenanceDowntimeFactsQueryHandler(db, owner).Handle(query, default);
        var fact = Assert.Single(response.Items);
        Assert.Equal(active.Id.ToString(), fact.WorkOrderId);
        Assert.Equal(From, fact.UnavailableFromUtc);
        Assert.Null(fact.ReleasedAtUtc);
        Assert.Null(fact.ExpectedRestoreAtUtc);
        Assert.Equal(expected.PredictedRestoreAtUtc, fact.PredictedRestoreAtUtc);
        Assert.Equal(expected.Source, fact.RestorePredictionSource);
        Assert.Equal(expected.SourceVersion, fact.RestorePredictionSourceVersion);
    }

    [Theory]
    [InlineData("organizationId")]
    [InlineData("environmentId")]
    [InlineData("deviceAssetIds")]
    [InlineData("windowEndUtc")]
    public async Task Public_query_rejects_incomplete_scope_and_invalid_window(string field)
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await using var factory = Factory(db);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var payload = new Dictionary<string, object?>
        {
            ["organizationId"] = "org", ["environmentId"] = "env", ["deviceAssetIds"] = new[] { "device" },
            ["windowStartUtc"] = From, ["windowEndUtc"] = From.AddHours(1),
        };
        payload[field] = field == "windowEndUtc" ? From : null;
        var response = await client.PostAsJsonAsync("/api/business/internal/v1/maintenance/downtime-facts/query", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_query_requires_service_authentication()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        await using var factory = Factory(db);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/business/internal/v1/maintenance/downtime-facts/query", Request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static MaintenanceWorkOrder Order(string org, string env, string device)
    {
        var order = MaintenanceWorkOrder.OpenManual(org, env, $"MWO-T-{Guid.NewGuid():N}", device, "high", "operator");
        order.MarkAssetUnavailable(From, "fault", From.AddHours(2));
        return order;
    }

    private static async Task<JsonElement[]> Read(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/business/internal/v1/maintenance/downtime-facts/query", Request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    private static WebApplicationFactory<Program> Factory(ApplicationDbContext db)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.AddSingleton(db);
            });
        });
        factory.UseKestrel(0);
        return factory;
    }
}
