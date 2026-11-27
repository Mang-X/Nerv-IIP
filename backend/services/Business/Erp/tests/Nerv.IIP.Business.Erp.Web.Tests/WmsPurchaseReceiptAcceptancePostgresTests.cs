using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class WmsPurchaseReceiptAcceptancePostgresTests
{
    private const string Route = "/api/business/v1/erp/wms-purchase-receipt-acceptances";

    [PurchaseReceiptPostgresFact]
    public async Task Committed_batch_can_be_read_and_retried_with_frozen_facts()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await ErpPostgresLaneDatabase.ResetCapSchemaAsync();
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = ErpPostgresLaneDatabase.ConnectionString,
            ["Persistence:AutoMigrate"] = "false",
            ["Messaging:Provider"] = "InMemory",
            ["InternalService:BearerToken"] = "test-acceptance-token",
        };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            var order = PurchaseOrder.Create("org-test", "env-test", "PO-WMS", "SUP-01", "SITE-01", "USD",
                [new PurchaseOrderLineDraft("10", "SKU-01", "pcs", 3m, 12.5m, new DateOnly(2026, 9, 1))],
                receivingAuthority: PurchaseReceiptInventoryPostingRoute.Wms);
            order.MarkApprovalRequested("approval-1");
            order.ReleaseAfterApproval("approval-1");
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
        }

        var batchId = Guid.NewGuid();
        var payload = new
        {
            organizationId = "org-test", environmentId = "env-test", wmsReceiptBatchId = batchId,
            purchaseOrderNo = "PO-WMS", siteCode = "SITE-01", exchangeRate = 1.25m,
            lines = new[] { new { purchaseOrderLineNo = "10", receivedQuantity = 2m } },
            unrestricted = false,
        };
        using var unauthorized = await client.PostAsJsonAsync(Route, payload);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-acceptance-token");

        var resultUrl = $"{Route}/{batchId}?organizationId=org-test&environmentId=env-test";
        using var absent = await client.GetAsync(resultUrl);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var response = await client.PostAsJsonAsync(Route, payload);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = accepted.RootElement.GetProperty("data");
        Assert.Equal(batchId, result.GetProperty("wmsReceiptBatchId").GetGuid());
        Assert.Equal(1.25m, result.GetProperty("exchangeRate").GetDecimal());
        var line = Assert.Single(result.GetProperty("lines").EnumerateArray());
        Assert.Equal(12.5m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(15.625m, line.GetProperty("unitCost").GetDecimal());
        Assert.Equal(2m, line.GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal("quality", line.GetProperty("qualityStatus").GetString());

        await using (var amend = factory.Services.CreateAsyncScope())
        {
            var db = amend.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.PurchaseOrders.Include(x => x.Lines).SingleAsync();
            var change = order.RequestChange([new PurchaseOrderLineChangeDraft("10", 3m, 99m, new DateOnly(2026, 9, 1))]);
            change.AssignApprovalChain("amend-1");
            order.ApplyApprovedChange("amend-1");
            await db.SaveChangesAsync();
        }

        using var replay = await client.PostAsJsonAsync(Route, payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var read = await client.GetAsync(resultUrl);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await read.Content.ReadAsStringAsync());
        using var otherScope = await client.GetAsync(resultUrl.Replace("org-test", "org-other", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, otherScope.StatusCode);

        await AssertRejectedAsync(client, payload with { exchangeRate = 1m });
        await AssertRejectedAsync(client, payload with { unrestricted = true });
        await AssertRejectedAsync(client, payload with
        {
            wmsReceiptBatchId = Guid.NewGuid(),
            lines = new[] { new { purchaseOrderLineNo = "10", receivedQuantity = 2m } },
        });
        await AssertRejectedAsync(client, payload with { wmsReceiptBatchId = Guid.NewGuid(), siteCode = "SITE-02" });
        await AssertRejectedAsync(client, payload with
        {
            wmsReceiptBatchId = Guid.NewGuid(),
            lines = new[] { new { purchaseOrderLineNo = "99", receivedQuantity = 1m } },
        });

        using var unrestricted = await client.PostAsJsonAsync(Route, payload with
        {
            wmsReceiptBatchId = Guid.NewGuid(), unrestricted = true,
            lines = new[] { new { purchaseOrderLineNo = "10", receivedQuantity = 1m } },
        });
        Assert.True(unrestricted.IsSuccessStatusCode, await unrestricted.Content.ReadAsStringAsync());
        using var unrestrictedResult = JsonDocument.Parse(await unrestricted.Content.ReadAsStringAsync());
        var unrestrictedLine = Assert.Single(unrestrictedResult.RootElement.GetProperty("data").GetProperty("lines").EnumerateArray());
        Assert.Equal("unrestricted", unrestrictedLine.GetProperty("qualityStatus").GetString());
        Assert.Equal(123.75m, unrestrictedLine.GetProperty("unitCost").GetDecimal());

        await using var verification = factory.Services.CreateAsyncScope();
        var verifiedDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await verifiedDb.PurchaseReceipts.CountAsync());
        var verifiedOrder = await verifiedDb.PurchaseOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(3m, Assert.Single(verifiedOrder.Lines).ReceivedQuantity);
        var outboxTopics = await verifiedDb.Database.SqlQueryRaw<string>(
            """SELECT "Name" AS "Value" FROM cap.published""").ToArrayAsync();
        Assert.Equal(2, outboxTopics.Count(topic => topic == "PurchaseReceiptRecordedIntegrationEvent"));
        Assert.DoesNotContain(outboxTopics, topic => topic == "InventoryMovementRequestedIntegrationEvent");
    }

    private static async Task AssertRejectedAsync(HttpClient client, object payload)
    {
        using var response = await client.PostAsJsonAsync(Route, payload);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
    }
}
