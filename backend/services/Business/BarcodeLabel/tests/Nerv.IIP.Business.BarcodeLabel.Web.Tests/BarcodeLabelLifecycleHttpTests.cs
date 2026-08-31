using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BarcodeLabelLifecycleHttpTests
{
    [Theory]
    [InlineData("dispatch", "{\"printBatchId\":\"{0}\",\"printerId\":\"printer-01\"}", false)]
    [InlineData("items/1/reprint", "{\"printBatchId\":\"{0}\",\"sequenceNo\":1,\"printerId\":\"printer-01\"}", true)]
    [InlineData("items/1/void", "{\"printBatchId\":\"{0}\",\"sequenceNo\":1,\"reason\":\"damaged\"}", false)]
    public async Task Existing_v1_lifecycle_requests_remain_compatible_without_scope_query(
        string action,
        string bodyTemplate,
        bool printedBatch)
    {
        await using var factory = CreateFactory(new PrintedPrinter());
        var printBatchId = await SeedBatchAsync(factory, "org-owner", "env-owner", printedBatch);
        using var client = CreateAuthenticatedClient(factory);
        using var body = JsonContent.Create(
            JsonDocument.Parse(bodyTemplate.Replace("{0}", printBatchId, StringComparison.Ordinal)).RootElement);

        using var response = await client.PostAsync(
            $"/api/business/v1/barcodes/print-batches/{printBatchId}/{action}",
            body);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), responseBody);
    }

    [Theory]
    [InlineData("dispatch", "{\"printBatchId\":\"{0}\",\"printerId\":\"printer-01\"}")]
    [InlineData("items/1/reprint", "{\"printBatchId\":\"{0}\",\"sequenceNo\":1,\"printerId\":\"printer-01\"}")]
    [InlineData("items/1/void", "{\"printBatchId\":\"{0}\",\"sequenceNo\":1,\"reason\":\"damaged\"}")]
    public async Task Lifecycle_http_endpoints_reject_a_batch_owned_by_another_scope(
        string action,
        string bodyTemplate)
    {
        await using var factory = CreateFactory(new PrintedPrinter());
        var printBatchId = await SeedBatchAsync(factory, "org-owner", "env-owner");
        using var client = CreateAuthenticatedClient(factory);
        using var body = JsonContent.Create(
            JsonDocument.Parse(bodyTemplate.Replace("{0}", printBatchId, StringComparison.Ordinal)).RootElement);

        using var response = await client.PostAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{printBatchId}/{action}" +
            "?organizationId=org-caller&environmentId=env-caller",
            body);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean(), responseBody);
    }

    [Fact]
    public async Task Reprint_http_response_does_not_expose_printer_exception_details()
    {
        await using var factory = CreateFactory(new ThrowingPrinter());
        var printBatchId = await SeedBatchAsync(factory, "org-001", "env-dev");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{printBatchId}/items/1/reprint" +
            "?organizationId=org-001&environmentId=env-dev",
            new { printBatchId, sequenceNo = 1, printerId = "printer-01" });

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), responseBody);
        var failureReason = document.RootElement
            .GetProperty("data")
            .GetProperty("failureReason")
            .GetString();
        Assert.Equal("打印机处理失败，请检查设备状态后重试。", failureReason);
        Assert.DoesNotContain("token=secret", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SeedBatchAsync(
        WebApplicationFactory<Program> factory,
        string organizationId,
        string environmentId,
        bool printed = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rule = BarcodeRule.Create(
            organizationId,
            environmentId,
            "FG",
            "code128",
            "FG",
            13,
            "none",
            ["wms.inbound"],
            "active");
        var batch = LabelPrintBatch.Create(
            organizationId,
            environmentId,
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "idem-print",
            "{}",
            1);
        if (printed)
        {
            batch.RecordSentToPrinter("printer-01", "initial-print-job");
            batch.RecordPrinted();
        }
        dbContext.LabelPrintBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        return batch.Id.ToString();
    }

    private static WebApplicationFactory<Program> CreateFactory(ILabelPrinter printer)
    {
        var databaseName = $"barcode-label-lifecycle-http-{Guid.CreateVersion7():N}";
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_barcode_lifecycle_http;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "barcode-label-lifecycle-http-test-token",
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.RemoveAll<IIntegrationEventPublisher>();
                    services.RemoveAll<ILabelPrinter>();
                    services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                    services.AddSingleton(printer);
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "barcode-label-lifecycle-http-test-token");
        return client;
    }

    private sealed class PrintedPrinter : ILabelPrinter
    {
        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<string> labelValues,
            CancellationToken cancellationToken) =>
            Task.FromResult(LabelPrinterDispatchResult.Printed("print-job-001"));
    }

    private sealed class ThrowingPrinter : ILabelPrinter
    {
        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<string> labelValues,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("token=secret");
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
