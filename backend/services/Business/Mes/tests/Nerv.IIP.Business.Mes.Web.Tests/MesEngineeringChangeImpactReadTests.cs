using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesEngineeringChangeImpactReadTests
{
    private const string Route = "/api/business/v1/mes/work-order-engineering-change-impacts?organizationId=org-a&environmentId=env-a";
    private static readonly DateOnly EffectiveDate = new(2026, 11, 1);
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Eco_read_returns_the_persisted_pending_impact_and_excludes_archive_markers()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route + "&changeNumber=ECO-01")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.EngineeringChangeWorkOrderImpacts.AddRange(
                Pending("org-a", "env-a", "WO-01", "ECO-01"),
                Pending("org-a", "env-a", "WO-01", "ECO-02"),
                MesEngineeringChangeWorkOrderImpact.ArchivedProductionVersion(
                    "org-a", "env-a", "ECO-01", "PV-01", "PV-02", EffectiveDate, DetectedAt));
            await db.SaveChangesAsync();
        }

        var result = await ReadAsync(client, "&changeNumber=ECO-01");
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(1, result.GetProperty("total").GetInt32());
        Assert.Equal("WO-01", item.GetProperty("workOrderId").GetString());
        Assert.Equal("ECO-01", item.GetProperty("changeNumber").GetString());
        Assert.Equal("pending-decision", item.GetProperty("disposition").GetString());
        Assert.Null(item.GetProperty("decidedBy").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("decidedAtUtc").ValueKind);
        Assert.Null(item.GetProperty("decisionReason").GetString());
    }

    [Fact]
    public async Task Work_order_read_returns_automatic_dispositions_and_manual_decision_audit()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        var decidedAt = DetectedAt.AddHours(2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var continued = Pending("org-a", "env-a", "WO-01", "ECO-03");
            continued.RecordDecision(MesEngineeringChangeDecisions.ContinueWithArchivedVersion, "planner-01", "已投料", decidedAt);
            var aborted = Pending("org-a", "env-a", "WO-01", "ECO-04");
            aborted.RecordDecision(MesEngineeringChangeDecisions.AbortWorkOrder, "planner-02", "旧版不能继续生产", decidedAt);
            db.EngineeringChangeWorkOrderImpacts.AddRange(
                MesEngineeringChangeWorkOrderImpact.AutoRebound(
                    "org-a", "env-a", "WO-01", "SKU-01", "created", "ECO-01", "PV-01", "PV-02", EffectiveDate, DetectedAt),
                MesEngineeringChangeWorkOrderImpact.BlockedForManualConfirmation(
                    "org-a", "env-a", "WO-01", "SKU-01", "released", "ECO-02", "PV-02", null, EffectiveDate, DetectedAt),
                continued, aborted, Pending("org-a", "env-a", "WO-OTHER", "ECO-01"));
            await db.SaveChangesAsync();
        }

        var result = await ReadAsync(client, "&workOrderId=WO-01");
        var items = result.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, result.GetProperty("total").GetInt32());
        Assert.Equal(
            ["auto-rebound", "blocked-for-manual-confirmation", "continue-with-archived-version", "abort-work-order"],
            items.Select(x => x.GetProperty("disposition").GetString()));
        foreach (var item in items.Take(2))
        {
            Assert.Null(item.GetProperty("decidedBy").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("decidedAtUtc").ValueKind);
            Assert.Null(item.GetProperty("decisionReason").GetString());
        }
        AssertDecision(items[2], "planner-01", "已投料", decidedAt);
        AssertDecision(items[3], "planner-02", "旧版不能继续生产", decidedAt);
    }

    [Fact]
    public async Task Scoped_reads_apply_tenant_and_task_visibility_before_paging()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.EngineeringChangeWorkOrderImpacts.AddRange(
                Pending("org-a", "env-a", "WO-00", "ECO-01"),
                Pending("org-a", "env-a", "WO-01", "ECO-01"),
                Pending("org-a", "env-a", "WO-02", "ECO-01"),
                Pending("org-a", "env-a", "WO-03", "ECO-01"),
                Pending("org-b", "env-a", "WO-01", "ECO-01"),
                Pending("org-a", "env-b", "WO-02", "ECO-01"));
            db.OperationTasks.AddRange(
                TaskFor("org-a", "env-a", "WO-01", "OP-01", "WC-OTHER", "user-a", "team-other"),
                TaskFor("org-a", "env-a", "WO-02", "OP-02", "WC-OTHER", "user-other", "team-a"),
                TaskFor("org-a", "env-a", "WO-03", "OP-03", "WC-A", "user-other", "team-other"),
                TaskFor("org-b", "env-a", "WO-00", "OP-04", "WC-A", "user-a", "team-a"),
                TaskFor("org-a", "env-b", "WO-00", "OP-05", "WC-A", "user-a", "team-a"));
            await db.SaveChangesAsync();
        }

        const string filters = "&changeNumber=ECO-01&authorizedAssignedUserIds=user-a&authorizedTeamIds=team-a&authorizedWorkCenterIds=WC-A";
        var firstPage = await ReadAsync(client, filters + "&take=2");
        Assert.Equal(3, firstPage.GetProperty("total").GetInt32());
        Assert.Equal(["WO-01", "WO-02"], firstPage.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("workOrderId").GetString()));
        var nextPage = await ReadAsync(client, filters + "&skip=2&take=2");
        Assert.Equal("WO-03", Assert.Single(nextPage.GetProperty("items").EnumerateArray()).GetProperty("workOrderId").GetString());
        await AssertEmptyAsync(client, filters + "&workOrderId=WO-00");
        await AssertEmptyAsync(client, "&changeNumber=ECO-01&authorizedTeamIds=%20");
        await AssertEmptyAsync(client, "&changeNumber=ECO-MISSING");
        await AssertEmptyAsync(client, "&workOrderId=WO-MISSING");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Route)).StatusCode);
    }

    private static OperationTask TaskFor(string org, string env, string order, string taskId, string center, string user, string team)
    {
        var task = OperationTask.Create(org, env, order, taskId, OperationTaskLifecycleStatus.Queued,
            10, center, [], DetectedAt, TimeSpan.FromHours(1), null, null, "SKU-01");
        task.Assign(user, null, null, DetectedAt, teamId: team);
        return task;
    }

    private static void AssertDecision(JsonElement item, string user, string reason, DateTimeOffset decidedAt)
    {
        Assert.Equal(user, item.GetProperty("decidedBy").GetString());
        Assert.Equal(reason, item.GetProperty("decisionReason").GetString());
        Assert.Equal(decidedAt, item.GetProperty("decidedAtUtc").GetDateTimeOffset());
    }

    private static async Task AssertEmptyAsync(HttpClient client, string filters)
    {
        var result = await ReadAsync(client, filters);
        Assert.Equal(0, result.GetProperty("total").GetInt32());
        Assert.Empty(result.GetProperty("items").EnumerateArray());
    }

    private static MesEngineeringChangeWorkOrderImpact Pending(string organizationId, string environmentId, string workOrderId, string changeNumber) =>
        MesEngineeringChangeWorkOrderImpact.PendingDecision(
            organizationId, environmentId, workOrderId, "SKU-01", "in-progress",
            changeNumber, "PV-01", "PV-02", EffectiveDate, DetectedAt);

    private static async Task<JsonElement> ReadAsync(HttpClient client, string filters)
    {
        using var response = await client.GetAsync(Route + filters);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                var databaseName = $"mes-eco-impact-read-{Guid.NewGuid():N}";
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        });
}
