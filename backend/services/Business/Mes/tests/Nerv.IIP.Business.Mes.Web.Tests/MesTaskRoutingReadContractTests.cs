using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesTaskRoutingReadContractTests
{
    // HttpApi / Regression: #4485, approved #4457 r2. Exercise real queries and HTTP serialization.
    [Fact]
    public async Task Http_reads_keep_creation_route_after_rebinding_and_leave_unproven_routes_null()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                var databaseName = $"mes-task-routing-read-{Guid.NewGuid():N}";
                services.RemoveAll<DbContextOptions<Infrastructure.ApplicationDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<Infrastructure.ApplicationDbContext>>();
                services.AddDbContext<Infrastructure.ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        var at = DateTimeOffset.Parse("2026-10-17T08:00:00Z");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
            var source = new ReboundRoutingSource();
            var create = new CreateRushWorkOrderCommandHandler(new PersistentMesPlanningStore(db),
                dbContext: db, routingSnapshotProvider: source);
            foreach (var revision in new[] { "A", "B" })
            {
                source.Revision = revision;
                await create.Handle(new("org-4485", "env-dev", $"WO-{revision}", "SKU-001", "PV-PINNED",
                    5m, at.AddDays(1), "WC-CUT", $"OP-{revision}", 10, TimeSpan.FromMinutes(20), at),
                    CancellationToken.None);
                await db.SaveChangesAsync();
            }
            db.OperationTasks.Add(OperationTask.Queue("org-4485", "env-dev", "WO-MANUAL", "OP-MANUAL",
                10, "WC-CUT", [], at, TimeSpan.FromMinutes(20), "SKU-001"));
            db.OperationTasks.Add(OperationTask.Queue("other-org", "env-dev", "WO-OTHER", "OP-OTHER-ORG",
                10, "WC-CUT", [], at, TimeSpan.FromMinutes(20), "SKU-001",
                routingIdentity: new("PV-OTHER", "opaque-other", "SECRET-ROUTE", "X")));
            db.OperationTasks.Add(OperationTask.Queue("org-4485", "other-env", "WO-OTHER", "OP-OTHER-ENV",
                10, "WC-CUT", [], at, TimeSpan.FromMinutes(20), "SKU-001",
                routingIdentity: new("PV-OTHER", "opaque-other", "SECRET-ROUTE", "X")));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        const string tenant = "organizationId=org-4485&environmentId=env-dev";
        using var response = await client.GetAsync($"/api/business/v1/mes/operation-tasks?{tenant}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, body.RootElement.GetProperty("total").GetInt32());
        var rows = body.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(x => x.GetProperty("operationTaskId").GetString()!);
        AssertRoute(rows["OP-A"], "ROUTE-EXPLICIT", "A");
        AssertRoute(rows["OP-B"], "ROUTE-EXPLICIT", "B");
        AssertRoute(rows["OP-MANUAL"], null, null);
        using var detail = await client.GetAsync($"/api/business/v1/mes/work-orders/WO-A?{tenant}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertRoute(Assert.Single(detailBody.RootElement.GetProperty("operationTasks").EnumerateArray()), "ROUTE-EXPLICIT", "A");

        using var openApi = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using var schema = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        var properties = schema.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(x => x.Value)
            .Single(x => x.TryGetProperty("properties", out var fields) && fields.TryGetProperty("actualLaborHours", out _))
            .GetProperty("properties");
        Assert.True(properties.TryGetProperty("routingCode", out _));
        Assert.True(properties.TryGetProperty("routingRevision", out _));

        client.DefaultRequestHeaders.Authorization = null;
        using var denied = await client.GetAsync($"/api/business/v1/mes/operation-tasks?{tenant}");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private static void AssertRoute(JsonElement row, string? code, string? revision)
    {
        Assert.Equal(code, row.GetProperty("routingCode").GetString());
        Assert.Equal(revision, row.GetProperty("routingRevision").GetString());
    }

    private sealed class ReboundRoutingSource : IMesRoutingSnapshotProvider
    {
        public string Revision { get; set; } = "A";

        public Task<MesRoutingSnapshotResult> GetSnapshotAsync(MesRoutingSnapshotRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("PV-PINNED", request.ProductionVersionId);
            return Task.FromResult(MesRoutingSnapshotResult.Captured("engineering-fixture",
                [new(10, "CUT", "WC-CUT", [], 20, false)], "PV-PINNED",
                new("PV-PINNED", Revision == "A" ? "opaque-first" : "opaque-second", "ROUTE-EXPLICIT", Revision)));
        }
    }
}
