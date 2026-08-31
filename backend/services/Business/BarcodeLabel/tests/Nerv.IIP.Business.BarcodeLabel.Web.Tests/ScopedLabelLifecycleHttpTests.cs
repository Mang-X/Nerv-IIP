using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ScopedLabelLifecycleHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task Scoped_dispatch_prints_only_the_batch_owned_by_the_required_scope()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed("scoped-job-001"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(factory, "org-001", "env-dev", "dispatch-owned");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{WireId(batch.Id)}/dispatch" +
            "?organizationId=org-001&environmentId=env-dev",
            JsonBody(new { printBatchId = batch.Id, printerId = "printer-01" }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Equal("printed", await GetBatchStatusAsync(client, batch.Id));
        Assert.Single(printer.Requests);
    }

    [Fact]
    public async Task Scoped_reprint_prints_only_the_owned_item()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed("scoped-reprint-001"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(factory, "org-001", "env-dev", "reprint-owned", printed: true);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{WireId(batch.Id)}/items/1/reprint" +
            "?organizationId=org-001&environmentId=env-dev",
            JsonBody(new { printBatchId = batch.Id, sequenceNo = 1, printerId = "printer-02" }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        var batchData = await GetBatchAsync(client, batch.Id);
        Assert.Equal("reprinted", batchData.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Single(printer.Requests);
    }

    [Fact]
    public async Task Scoped_void_changes_only_the_owned_item()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed("unused"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(factory, "org-001", "env-dev", "void-owned");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{WireId(batch.Id)}/items/1/void" +
            "?organizationId=org-001&environmentId=env-dev",
            JsonBody(new { printBatchId = batch.Id, sequenceNo = 1, reason = "标签破损" }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        var batchData = await GetBatchAsync(client, batch.Id);
        Assert.Equal("voided", batchData.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Empty(printer.Requests);
    }

    public static TheoryData<LifecycleOperation, string> MissingOrBlankScopeCases => new()
    {
        { LifecycleOperation.Dispatch, "" },
        { LifecycleOperation.Dispatch, "?organizationId=org-001" },
        { LifecycleOperation.Dispatch, "?environmentId=env-dev" },
        { LifecycleOperation.Dispatch, "?organizationId=%20%20&environmentId=env-dev" },
        { LifecycleOperation.Dispatch, "?organizationId=org-001&environmentId=%20%20" },
        { LifecycleOperation.Reprint, "" },
        { LifecycleOperation.Reprint, "?organizationId=org-001" },
        { LifecycleOperation.Reprint, "?environmentId=env-dev" },
        { LifecycleOperation.Reprint, "?organizationId=%20%20&environmentId=env-dev" },
        { LifecycleOperation.Reprint, "?organizationId=org-001&environmentId=%20%20" },
        { LifecycleOperation.Void, "" },
        { LifecycleOperation.Void, "?organizationId=org-001" },
        { LifecycleOperation.Void, "?environmentId=env-dev" },
        { LifecycleOperation.Void, "?organizationId=%20%20&environmentId=env-dev" },
        { LifecycleOperation.Void, "?organizationId=org-001&environmentId=%20%20" },
    };

    [Theory]
    [MemberData(nameof(MissingOrBlankScopeCases))]
    public async Task Scoped_lifecycle_rejects_missing_or_blank_scope_before_any_business_action(
        LifecycleOperation operation,
        string query)
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed("must-not-run"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(factory, "org-001", "env-dev", $"missing-scope-{operation}", printed: true);
        using var client = CreateAuthenticatedClient(factory);
        var before = await GetBatchAsync(client, batch.Id);

        using var response = await PostLifecycleAsync(client, operation, batch.Id, query);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Equal(400, result.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(before.GetRawText(), (await GetBatchAsync(client, batch.Id)).GetRawText());
        Assert.Empty(printer.Requests);
    }

    [Theory]
    [InlineData(LifecycleOperation.Dispatch)]
    [InlineData(LifecycleOperation.Reprint)]
    [InlineData(LifecycleOperation.Void)]
    public async Task Scoped_lifecycle_hides_batches_owned_by_another_scope(LifecycleOperation operation)
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed("must-not-run"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(factory, "org-002", "env-dev", $"cross-scope-{operation}", printed: true);
        using var client = CreateAuthenticatedClient(factory);
        var before = await GetBatchAsync(client, batch.Id);

        using var response = await PostLifecycleAsync(
            client,
            operation,
            batch.Id,
            "?organizationId=org-001&environmentId=env-dev");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Contains("未找到打印批次", result.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(WireId(batch.Id), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.GetRawText(), (await GetBatchAsync(client, batch.Id)).GetRawText());
        Assert.Empty(printer.Requests);
    }

    [Theory]
    [InlineData(LifecycleOperation.Dispatch)]
    [InlineData(LifecycleOperation.Reprint)]
    [InlineData(LifecycleOperation.Void)]
    public async Task Scoped_lifecycle_uses_route_identifiers_when_body_identifiers_conflict(LifecycleOperation operation)
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed($"route-wins-{operation}"));
        await using var factory = CreateFactory(printer);
        var routeBatch = await SeedBatchAsync(
            factory,
            "org-001",
            "env-dev",
            $"route-target-{operation}",
            printed: operation == LifecycleOperation.Reprint);
        var bodyBatch = await SeedBatchAsync(
            factory,
            "org-001",
            "env-dev",
            $"body-target-{operation}",
            printed: operation == LifecycleOperation.Reprint);
        using var client = CreateAuthenticatedClient(factory);
        var bodyBefore = await GetBatchAsync(client, bodyBatch.Id);

        using var response = await PostLifecycleAsync(
            client,
            operation,
            routeBatch.Id,
            "?organizationId=org-001&environmentId=env-dev",
            bodyBatch.Id,
            bodySequenceNo: 999);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        var routeAfter = await GetBatchAsync(client, routeBatch.Id);
        var expectedStatus = operation switch
        {
            LifecycleOperation.Dispatch => "printed",
            LifecycleOperation.Reprint => "reprinted",
            LifecycleOperation.Void => "voided",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        var actualStatus = operation == LifecycleOperation.Dispatch
            ? routeAfter.GetProperty("status").GetString()
            : routeAfter.GetProperty("items")[0].GetProperty("status").GetString();
        Assert.Equal(expectedStatus, actualStatus);
        Assert.Equal(bodyBefore.GetRawText(), (await GetBatchAsync(client, bodyBatch.Id)).GetRawText());
    }

    [Theory]
    [InlineData(LifecycleOperation.Dispatch)]
    [InlineData(LifecycleOperation.Reprint)]
    public async Task Scoped_print_failures_do_not_expose_printer_adapter_secrets(LifecycleOperation operation)
    {
        var printer = new ThrowingPrinter(
            "token=super-secret https://printer.internal.example:9100 host=printer.internal.example");
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(
            factory,
            "org-001",
            "env-dev",
            $"safe-printer-failure-{operation}",
            printed: operation == LifecycleOperation.Reprint);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await PostLifecycleAsync(
            client,
            operation,
            batch.Id,
            "?organizationId=org-001&environmentId=env-dev");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var visibleOutput = body + (await GetBatchAsync(client, batch.Id)).GetRawText();
        Assert.Contains("打印服务暂时不可用", visibleOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", visibleOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", visibleOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("printer.internal.example", visibleOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=", visibleOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(LifecycleOperation.Dispatch)]
    [InlineData(LifecycleOperation.Reprint)]
    [InlineData(LifecycleOperation.Void)]
    public async Task Legacy_v1_lifecycle_keeps_working_without_scope_query(LifecycleOperation operation)
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Printed($"legacy-{operation}"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedBatchAsync(
            factory,
            "org-001",
            "env-dev",
            $"legacy-no-scope-{operation}",
            printed: operation == LifecycleOperation.Reprint);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await PostLegacyLifecycleAsync(client, operation, batch.Id);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
    }

    private static WebApplicationFactory<Program> CreateFactory(ILabelPrinter printer)
    {
        var databaseName = $"barcode-label-scoped-lifecycle-http-{Guid.CreateVersion7():N}";
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_barcode_scoped_http;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "barcode-label-scoped-http-test-token",
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

    private static async Task<LabelPrintBatch> SeedBatchAsync(
        WebApplicationFactory<Program> factory,
        string organizationId,
        string environmentId,
        string idempotencyKey,
        bool printed = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rule = BarcodeRule.Create(
            organizationId,
            environmentId,
            $"RULE-{Guid.CreateVersion7():N}",
            "code128",
            "SC",
            40,
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
            idempotencyKey,
            "{}",
            1);
        if (printed)
        {
            batch.RecordSentToPrinter("seed-printer", "seed-job");
            batch.RecordPrinted();
        }

        dbContext.AddRange(rule, batch);
        await dbContext.SaveChangesAsync();
        return batch;
    }

    private static async Task<string> GetBatchStatusAsync(HttpClient client, LabelPrintBatchId printBatchId)
    {
        return (await GetBatchAsync(client, printBatchId)).GetProperty("status").GetString()!;
    }

    private static async Task<JsonElement> GetBatchAsync(HttpClient client, LabelPrintBatchId printBatchId)
    {
        using var response = await client.GetAsync($"/api/business/v1/barcodes/print-batches/{WireId(printBatchId)}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), body);
        return document.RootElement.GetProperty("data").GetProperty("printBatch").Clone();
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "barcode-label-scoped-http-test-token");
        return client;
    }

    private static Task<HttpResponseMessage> PostLifecycleAsync(
        HttpClient client,
        LifecycleOperation operation,
        LabelPrintBatchId printBatchId,
        string query,
        LabelPrintBatchId? bodyPrintBatchId = null,
        int bodySequenceNo = 1)
    {
        var id = WireId(printBatchId);
        var bodyId = bodyPrintBatchId ?? printBatchId;
        return operation switch
        {
            LifecycleOperation.Dispatch => client.PostAsync(
                $"/api/business/internal/v1/barcodes/print-batches/{id}/dispatch{query}",
                JsonBody(new { printBatchId = bodyId, printerId = "printer-01" })),
            LifecycleOperation.Reprint => client.PostAsync(
                $"/api/business/internal/v1/barcodes/print-batches/{id}/items/1/reprint{query}",
                JsonBody(new { printBatchId = bodyId, sequenceNo = bodySequenceNo, printerId = "printer-01" })),
            LifecycleOperation.Void => client.PostAsync(
                $"/api/business/internal/v1/barcodes/print-batches/{id}/items/1/void{query}",
                JsonBody(new { printBatchId = bodyId, sequenceNo = bodySequenceNo, reason = "标签破损" })),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private static Task<HttpResponseMessage> PostLegacyLifecycleAsync(
        HttpClient client,
        LifecycleOperation operation,
        LabelPrintBatchId printBatchId)
    {
        var id = WireId(printBatchId);
        return operation switch
        {
            LifecycleOperation.Dispatch => client.PostAsync(
                $"/api/business/v1/barcodes/print-batches/{id}/dispatch",
                JsonBody(new { printBatchId, printerId = "printer-legacy" })),
            LifecycleOperation.Reprint => client.PostAsync(
                $"/api/business/v1/barcodes/print-batches/{id}/items/1/reprint",
                JsonBody(new { printBatchId, sequenceNo = 1, printerId = "printer-legacy" })),
            LifecycleOperation.Void => client.PostAsync(
                $"/api/business/v1/barcodes/print-batches/{id}/items/1/void",
                JsonBody(new { printBatchId, sequenceNo = 1, reason = "标签破损" })),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static string WireId(LabelPrintBatchId id)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(id, JsonOptions));
        return document.RootElement.GetString()!;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddNetCorePalJsonConverters();
        return options;
    }

    private sealed class RecordingPrinter(LabelPrinterDispatchResult result) : ILabelPrinter
    {
        public List<PrintRequest> Requests { get; } = [];

        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<string> labelValues,
            CancellationToken cancellationToken)
        {
            Requests.Add(new PrintRequest(printerId, labelValues.ToArray()));
            return Task.FromResult(result);
        }
    }

    private sealed record PrintRequest(string PrinterId, IReadOnlyCollection<string> LabelValues);

    private sealed class ThrowingPrinter(string message) : ILabelPrinter
    {
        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<string> labelValues,
            CancellationToken cancellationToken) => throw new InvalidOperationException(message);
    }

    public enum LifecycleOperation
    {
        Dispatch,
        Reprint,
        Void,
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
