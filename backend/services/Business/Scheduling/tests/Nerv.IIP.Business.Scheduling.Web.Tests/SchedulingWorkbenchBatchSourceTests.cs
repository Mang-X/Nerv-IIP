using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed partial class SchedulingWorkbenchTests
{
    // Regression and ApprovedDecision: #4133 requires complete paging and one assembly batch for 500 orders.
    [Fact]
    public async Task Source_provider_resolves_selected_orders_across_three_pages_without_exact_lookups()
    {
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var orders = Enumerable.Range(1, 1020).Select(i => WorkOrder($"WO-{i:0000}", start)).ToArray();
        var skips = new List<int>();
        var exactLookups = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            if (query.ContainsKey("workOrderId"))
            {
                exactLookups++;
                return Json(new { items = Array.Empty<object>(), total = 0 });
            }
            var skip = int.Parse(query["skip"].ToString());
            var take = int.Parse(query["take"].ToString());
            skips.Add(skip);
            return Json(new { items = orders.Skip(skip).Take(take).ToArray(), total = orders.Length });
        })) { BaseAddress = new Uri("http://mes") };
        var provider = new HttpSchedulingWorkbenchSourceProvider(client, new StubProductEngineeringClient());

        var result = await provider.ResolveOrdersAsync("org-001", "env-dev", start,
            [new("WO-0020", 0, false), new("WO-0520", 0, false), new("WO-1020", 0, false)], default);

        Assert.Equal(["WO-0020", "WO-0520", "WO-1020"], result.Select(x => x.Order.OrderId));
        Assert.Equal([0, 500, 1000], skips);
        Assert.Equal(0, exactLookups);
    }

    [Fact]
    public async Task Five_hundred_orders_use_one_assembly_and_readiness_batch_and_keep_problem_semantics()
    {
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(1, 500).Select(i => $"WO-{i:0000}").ToArray();
        var handler = new AssemblyBatchHandler(ids, start);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://mes") };
        var routing = new SchedulingProblemRoutingSnapshot("ROUTE-001", "A", "SKU-001",
            [new SchedulingProblemRoutingOperationSnapshot(10, "WC-001", "cutting", "Cutting", 0, 30, 0)]);
        var engineering = new StubProductEngineeringClient(routing);
        var masterData = new StubMasterDataClient(start);
        var provider = new HttpSchedulingWorkbenchSourceProvider(client, engineering);

        var orders = await provider.ResolveOrdersAsync("org-001", "env-dev", start,
            ids.Select(id => new SchedulingWorkbenchOrderSelection(id, 999, true)).ToArray(), default);
        var problem = await new SchedulingProblemProducer(engineering, masterData).AssembleWorkbenchAsync(
            new("batch-problem", "org-001", "env-dev", start, start.AddHours(8), orders), default);
        var readiness = await new HttpSchedulingMaterialReadinessProvider(new BatchClientFactory(client), null,
            NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance).QueryAsync(problem, default);

        Assert.Equal(500, problem.Orders.Count);
        Assert.Equal([new SchedulingAssemblyDependencyContract("WO-0002", "WO-0001"),
            new SchedulingAssemblyDependencyContract("OUTSIDE-SELECTION", "WO-0003")], problem.AssemblyDependencies);
        Assert.All(problem.Orders, order =>
        {
            Assert.Equal(10, order.Priority);
            Assert.False(order.IsRush);
            Assert.Equal(order.OrderId, order.BusinessReference);
            Assert.Equal($"{order.OrderId}-OP10", Assert.Single(order.Operations).OperationId);
        });
        Assert.Empty(readiness);
        Assert.Equal(1, handler.AssemblyBatches);
        Assert.Equal(1, handler.ReadinessBatches);
        Assert.Equal(1, engineering.ProductionVersionReads);
        Assert.Equal(1, engineering.RoutingReads);
        Assert.Equal(1, masterData.WorkCenterReads);
        Assert.Equal(1, masterData.CalendarReads);
        Assert.Equal(1, masterData.DeviceReads);
        Assert.Equal(1, masterData.ToolingReads);
    }

    private sealed class BatchClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class AssemblyBatchHandler(string[] ids, DateTimeOffset start) : HttpMessageHandler
    {
        public int AssemblyBatches { get; private set; }
        public int ReadinessBatches { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/work-orders", StringComparison.Ordinal))
                return Json(new { items = ids.Select(id => WorkOrder(id, start)).ToArray(), total = ids.Length });

            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("org-001", body.RootElement.GetProperty("organizationId").GetString());
            Assert.Equal("env-dev", body.RootElement.GetProperty("environmentId").GetString());
            Assert.Equal(ids, body.RootElement.GetProperty("workOrderIds").EnumerateArray().Select(x => x.GetString()));
            if (request.RequestUri!.AbsolutePath.EndsWith("/assembly-children/batch", StringComparison.Ordinal))
            {
                AssemblyBatches++;
                // Response order is deliberately different from request order: map by authoritative work-order identity.
                return Json(new BatchAssemblyChildWorkOrdersResponse(ids.Reverse().Select(id =>
                    new AssemblyChildWorkOrdersItem(id, id switch
                    {
                        "WO-0001" => ["WO-0002"],
                        "WO-0003" => ["OUTSIDE-SELECTION"],
                        _ => []
                    })).ToArray()));
            }
            Assert.EndsWith("/material-readiness/batch", request.RequestUri.AbsolutePath);
            ReadinessBatches++;
            return Json(new { items = ids.Select(id => new { workOrderId = id, readinessStatus = "Ready",
                blockingReasons = Array.Empty<string>(), items = Array.Empty<object>() }).ToArray() });
        }
    }
}
