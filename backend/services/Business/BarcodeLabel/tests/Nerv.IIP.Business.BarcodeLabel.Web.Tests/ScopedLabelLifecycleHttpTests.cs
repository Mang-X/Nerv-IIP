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
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ScopedLabelLifecycleHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private const string VariableSchemaJson =
        """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""";
    private const string TemplateJson =
        """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"skuCode"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
    private static readonly string AssetSha256 = $"sha256:{new string('a', 64)}";

    [Fact]
    public async Task Scoped_v2_idempotency_key_detail_returns_the_same_complete_batch_without_writes()
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedGs1BatchAsync(factory, "org-001", "env-dev", "report-intent:Case/A");
        using var client = CreateAuthenticatedClient(factory);

        using var byIdResponse = await client.GetAsync(
            $"/api/business/v2/barcodes/print-batches/{WireId(batch.Id)}" +
            "?organizationId=org-001&environmentId=env-dev");
        using var firstByKeyResponse = await client.GetAsync(
            "/api/business/v2/barcodes/print-batches/by-idempotency-key" +
            "?organizationId=org-001&environmentId=env-dev&idempotencyKey=report-intent%3ACase%2FA");
        using var secondByKeyResponse = await client.GetAsync(
            "/api/business/v2/barcodes/print-batches/by-idempotency-key" +
            "?organizationId=org-001&environmentId=env-dev&idempotencyKey=report-intent%3ACase%2FA");

        var byIdBody = await byIdResponse.Content.ReadAsStringAsync();
        var firstByKeyBody = await firstByKeyResponse.Content.ReadAsStringAsync();
        var secondByKeyBody = await secondByKeyResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstByKeyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondByKeyResponse.StatusCode);
        using var byId = JsonDocument.Parse(byIdBody);
        using var firstByKey = JsonDocument.Parse(firstByKeyBody);
        using var secondByKey = JsonDocument.Parse(secondByKeyBody);
        var expectedDetail = byId.RootElement.GetProperty("data").GetProperty("printBatch").GetRawText();
        Assert.Equal(expectedDetail, firstByKey.RootElement.GetProperty("data").GetProperty("printBatch").GetRawText());
        Assert.Equal(expectedDetail, secondByKey.RootElement.GetProperty("data").GetProperty("printBatch").GetRawText());
        Assert.Equal(
            "opaque:report-intent-a",
            firstByKey.RootElement.GetProperty("data").GetProperty("printBatch")
                .GetProperty("reportIntentFingerprint").GetString());

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await verificationDb.LabelPrintBatches.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Scoped_v2_idempotency_key_detail_matches_the_key_and_scope_exactly()
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var expected = await SeedReservedBatchAsync(factory, "org-owner", "env-owner", "intent:Case/A");
        _ = await SeedReservedBatchAsync(factory, "org-owner", "env-owner", "intent:Case/A-extra");
        _ = await SeedReservedBatchAsync(factory, "org-other", "env-owner", "intent:Case/A");
        _ = await SeedReservedBatchAsync(factory, "org-owner", "env-other", "intent:Case/A");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            "/api/business/v2/barcodes/print-batches/by-idempotency-key" +
            "?organizationId=org-owner&environmentId=env-owner&idempotencyKey=intent%3ACase%2FA");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Equal(
            WireId(expected.Id),
            result.RootElement.GetProperty("data").GetProperty("printBatch").GetProperty("printBatchId").GetString());
    }

    [Theory]
    [InlineData("org-other", "env-owner", "intent-owned")]
    [InlineData("org-owner", "env-other", "intent-owned")]
    [InlineData("org-owner", "env-owner", "intent-unknown")]
    public async Task Scoped_v2_idempotency_key_detail_uses_one_non_disclosing_not_found_result(
        string organizationId,
        string environmentId,
        string idempotencyKey)
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        _ = await SeedReservedBatchAsync(factory, "org-owner", "env-owner", "intent-owned");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            "/api/business/v2/barcodes/print-batches/by-idempotency-key?" +
            $"organizationId={organizationId}&environmentId={environmentId}&idempotencyKey={idempotencyKey}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Equal("未找到打印批次。", result.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain(WireId((await GetOnlyBatchAsync(factory)).Id), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("intent-owned", body, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque:report-intent-a", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scoped_v2_detail_returns_ordered_serial_gs1_mes_and_transport_facts()
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedGs1BatchAsync(factory, "org-001", "env-dev", "report-intent-detail");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            $"/api/business/v2/barcodes/print-batches/{WireId(batch.Id)}" +
            "?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        var detail = result.RootElement.GetProperty("data").GetProperty("printBatch");
        Assert.Equal("report-intent-detail", detail.GetProperty("reportIntentKey").GetString());
        Assert.Equal("opaque:report-intent-a", detail.GetProperty("reportIntentFingerprint").GetString());
        Assert.Equal("sent-to-printer", detail.GetProperty("status").GetString());
        Assert.Equal("printer-01", detail.GetProperty("printerId").GetString());
        Assert.Equal("job-001", detail.GetProperty("printJobId").GetString());
        Assert.Equal("report-id-001", detail.GetProperty("productionReportId").GetString());
        Assert.Equal("PR-001", detail.GetProperty("productionReportNo").GetString());
        var items = detail.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([1, 2], items.Select(item => item.GetProperty("sequenceNo").GetInt32()).ToArray());
        Assert.Equal(
            ["00000000001", "00000000002"],
            items.Select(item => item.GetProperty("serialNumber").GetString()!).ToArray());
        Assert.All(items, item =>
        {
            Assert.Equal("created", item.GetProperty("status").GetString());
            Assert.Equal("LOT-A", item.GetProperty("lotNo").GetString());
            Assert.Equal("09506000134352", item.GetProperty("gtin").GetString());
            Assert.EndsWith(
                $".{item.GetProperty("serialNumber").GetString()}",
                item.GetProperty("epcUri").GetString(),
                StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("org-other", "env-owner")]
    [InlineData("org-owner", "env-other")]
    public async Task Scoped_v2_detail_hides_a_batch_owned_by_another_scope(
        string organizationId,
        string environmentId)
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedReservedBatchAsync(factory, "org-owner", "env-owner", "report-intent-owned");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            $"/api/business/v2/barcodes/print-batches/{WireId(batch.Id)}" +
            $"?organizationId={organizationId}&environmentId={environmentId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.DoesNotContain(WireId(batch.Id), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("report-intent-owned", body, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque:report-intent-a", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scoped_v2_detail_returns_non_empty_failure_and_void_reasons()
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedGs1BatchAsync(
            factory,
            "org-001",
            "env-dev",
            "report-intent-failed",
            failedAndVoided: true);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            $"/api/business/v2/barcodes/print-batches/{WireId(batch.Id)}" +
            "?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        var detail = result.RootElement.GetProperty("data").GetProperty("printBatch");
        Assert.Equal("failed", detail.GetProperty("status").GetString());
        Assert.Equal("打印机离线。", detail.GetProperty("failureReason").GetString());
        var item = detail.GetProperty("items")[0];
        Assert.Equal("voided", item.GetProperty("status").GetString());
        Assert.Equal("标签破损。", item.GetProperty("voidReason").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?organizationId=org-001")]
    [InlineData("?environmentId=env-dev")]
    [InlineData("?organizationId=%20%20&environmentId=env-dev")]
    [InlineData("?organizationId=org-001&environmentId=%20%20")]
    public async Task Scoped_v2_detail_rejects_missing_or_blank_scope(string query)
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedReservedBatchAsync(factory, "org-001", "env-dev", "report-intent-required-scope");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            $"/api/business/v2/barcodes/print-batches/{WireId(batch.Id)}{query}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean(), body);
        Assert.Equal(400, result.RootElement.GetProperty("code").GetInt32());
        Assert.DoesNotContain("report-intent-required-scope", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_v1_detail_keeps_the_original_unscoped_response_contract()
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedReservedBatchAsync(factory, "org-001", "env-dev", "legacy-detail-no-scope");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.GetAsync(
            $"/api/business/v1/barcodes/print-batches/{WireId(batch.Id)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(body);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), body);
        var detail = result.RootElement.GetProperty("data").GetProperty("printBatch");
        Assert.False(detail.TryGetProperty("reportIntentKey", out _));
        Assert.False(detail.TryGetProperty("reportIntentFingerprint", out _));
        Assert.False(detail.TryGetProperty("productionReportId", out _));
        Assert.False(detail.TryGetProperty("productionReportNo", out _));
        var item = detail.GetProperty("items")[0];
        Assert.False(item.TryGetProperty("serialNumber", out _));
        Assert.False(item.TryGetProperty("lotNo", out _));
        Assert.False(item.TryGetProperty("gtin", out _));
        Assert.False(item.TryGetProperty("epcUri", out _));
    }

    [Fact]
    public async Task Reserved_batch_is_rejected_by_dispatch_then_activation_enables_the_existing_batch()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("activation-job-001"));
        await using var factory = CreateFactory(printer);
        var batch = await SeedReservedBatchAsync(factory, "org-001", "env-dev", "report-intent-001");
        using var client = CreateAuthenticatedClient(factory);
        var scopeQuery = "?organizationId=org-001&environmentId=env-dev";

        using var rejectedDispatch = await PostLifecycleAsync(
            client,
            LifecycleOperation.Dispatch,
            batch.Id,
            scopeQuery);
        var rejectedBody = await rejectedDispatch.Content.ReadAsStringAsync();
        using var rejectedResult = JsonDocument.Parse(rejectedBody);
        Assert.False(rejectedResult.RootElement.GetProperty("success").GetBoolean(), rejectedBody);
        Assert.Empty(printer.Requests);

        using var activation = await client.PostAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{WireId(batch.Id)}/activate{scopeQuery}",
            JsonBody(new { productionReportId = "report-id-001", productionReportNo = "PR-001" }));
        var activationBody = await activation.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        using var activationResult = JsonDocument.Parse(activationBody);
        Assert.True(activationResult.RootElement.GetProperty("success").GetBoolean(), activationBody);

        using var acceptedDispatch = await PostLifecycleAsync(
            client,
            LifecycleOperation.Dispatch,
            batch.Id,
            scopeQuery);
        var acceptedBody = await acceptedDispatch.Content.ReadAsStringAsync();
        using var acceptedResult = JsonDocument.Parse(acceptedBody);
        Assert.True(acceptedResult.RootElement.GetProperty("success").GetBoolean(), acceptedBody);
        Assert.Single(printer.Requests);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDb.LabelPrintBatches.AsNoTracking().SingleAsync(x => x.Id == batch.Id);
        Assert.Equal("sent-to-printer", persisted.Status);
        Assert.Equal("report-id-001", persisted.ProductionReportId);
        Assert.Equal("PR-001", persisted.ProductionReportNo);
    }

    [Fact]
    public async Task Activation_does_not_expose_or_change_a_reserved_batch_from_another_scope()
    {
        await using var factory = CreateFactory(new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused")));
        var batch = await SeedReservedBatchAsync(factory, "org-owner", "env-owner", "report-intent-owned");
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsync(
            $"/api/business/internal/v1/barcodes/print-batches/{WireId(batch.Id)}/activate" +
            "?organizationId=org-other&environmentId=env-owner",
            JsonBody(new { productionReportId = "report-id-other", productionReportNo = "PR-OTHER" }));
        var body = await response.Content.ReadAsStringAsync();
        using var result = JsonDocument.Parse(body);

        Assert.False(result.RootElement.GetProperty("success").GetBoolean(), body);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDb.LabelPrintBatches.AsNoTracking().SingleAsync(x => x.Id == batch.Id);
        Assert.Equal("reserved", persisted.Status);
        Assert.Null(persisted.ProductionReportId);
        Assert.Null(persisted.ProductionReportNo);
    }

    [Fact]
    public async Task Scoped_dispatch_prints_only_the_batch_owned_by_the_required_scope()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("scoped-job-001"));
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
        Assert.Equal("sent-to-printer", await GetBatchStatusAsync(client, batch.Id));
        Assert.Single(printer.Requests);
    }

    [Fact]
    public async Task Scoped_reprint_prints_only_the_owned_item()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("scoped-reprint-001"));
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
        Assert.Equal("printed", batchData.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Single(printer.Requests);
    }

    [Fact]
    public async Task Scoped_void_changes_only_the_owned_item()
    {
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("unused"));
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
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("must-not-run"));
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
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("must-not-run"));
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
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent($"route-wins-{operation}"));
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
            LifecycleOperation.Dispatch => "sent-to-printer",
            LifecycleOperation.Reprint => "printed",
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
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent($"legacy-{operation}"));
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
                    services.RemoveAll<ILabelTemplateAssetPort>();
                    services.RemoveAll<ILabelPrintBatchActivationFence>();
                    services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                    services.AddSingleton(printer);
                    services.AddSingleton<ILabelTemplateAssetPort, FixedTemplateAssetPort>();
                    services.AddSingleton<ILabelPrintBatchActivationFence, NoopActivationFence>();
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
    }

    private static async Task<LabelPrintBatch> SeedReservedBatchAsync(
        WebApplicationFactory<Program> factory,
        string organizationId,
        string environmentId,
        string reportIntentKey)
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
        var template = LabelTemplate.Create(
            organizationId,
            environmentId,
            $"TEMPLATE-{Guid.CreateVersion7():N}",
            "Lifecycle test template",
            "file-template-001",
            VariableSchemaJson,
            "active");
        var batch = LabelPrintBatch.CreateWithAllocatedSerialNumbers(
            organizationId,
            environmentId,
            rule,
            template.Id,
            new LabelPrintBatchSnapshot(
                template.TemplateFileId,
                AssetSha256,
                VariableSchemaJson,
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            reportIntentKey,
            "opaque:report-intent-a",
            """{"skuCode":"SKU-FG-1000"}""",
            1,
            ["00000000001"]);
        dbContext.AddRange(rule, template, batch);
        await dbContext.SaveChangesAsync();
        return batch;
    }

    private static async Task<LabelPrintBatch> GetOnlyBatchAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.LabelPrintBatches.AsNoTracking().SingleAsync();
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
        var template = LabelTemplate.Create(
            organizationId,
            environmentId,
            $"TEMPLATE-{Guid.CreateVersion7():N}",
            "Lifecycle test template",
            "file-template-001",
            VariableSchemaJson,
            "active");
        var batch = LabelPrintBatch.ReconstituteHistorical(
            organizationId,
            environmentId,
            rule,
            template.Id,
            new LabelPrintBatchSnapshot(
                template.TemplateFileId,
                AssetSha256,
                VariableSchemaJson,
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            idempotencyKey,
            """{"skuCode":"SKU-FG-1000"}""",
            1);
        if (printed)
        {
            batch.RecordSentToPrinter("seed-printer", "seed-job");
            batch.RecordPrinted();
        }

        dbContext.AddRange(rule, template, batch);
        await dbContext.SaveChangesAsync();
        return batch;
    }

    private static async Task<LabelPrintBatch> SeedGs1BatchAsync(
        WebApplicationFactory<Program> factory,
        string organizationId,
        string environmentId,
        string reportIntentKey,
        bool failedAndVoided = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rule = BarcodeRule.Create(
            organizationId,
            environmentId,
            $"RULE-{Guid.CreateVersion7():N}",
            "gs1-128",
            "0950600013435",
            80,
            "gs1-mod10",
            ["wms.inbound"],
            "active",
            7);
        var template = LabelTemplate.Create(
            organizationId,
            environmentId,
            $"TEMPLATE-{Guid.CreateVersion7():N}",
            "Scoped detail test template",
            "file-template-001",
            VariableSchemaJson,
            "active");
        var batch = LabelPrintBatch.CreateWithAllocatedSerialNumbers(
            organizationId,
            environmentId,
            rule,
            template.Id,
            new LabelPrintBatchSnapshot(
                template.TemplateFileId,
                AssetSha256,
                VariableSchemaJson,
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            reportIntentKey,
            "opaque:report-intent-a",
            """{"lotNo":"LOT-A"}""",
            2,
            ["00000000001", "00000000002"]);
        batch.Activate("report-id-001", "PR-001");
        if (failedAndVoided)
        {
            batch.VoidItem(1, "标签破损。");
            batch.RecordPrintFailed("printer-01", "打印机离线。");
        }
        else
        {
            batch.RecordSentToPrinter("printer-01", "job-001");
        }
        dbContext.AddRange(rule, template, batch);
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

    private sealed class NoopActivationFence : ILabelPrintBatchActivationFence
    {
        public Task AcquireAsync(
            string organizationId,
            string environmentId,
            LabelPrintBatchId printBatchId,
            CancellationToken cancellationToken) => Task.CompletedTask;
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
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            Requests.Add(new PrintRequest(
                printerId,
                documents.Select(document => document.Payload.ToArray()).ToArray()));
            return Task.FromResult(result);
        }
    }

    private sealed record PrintRequest(string PrinterId, IReadOnlyCollection<byte[]> Documents);

    private sealed class ThrowingPrinter(string message) : ILabelPrinter
    {
        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken) => throw new InvalidOperationException(message);
    }

    private sealed class FixedTemplateAssetPort : ILabelTemplateAssetPort
    {
        public Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
            LabelTemplateAssetReference reference,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, TemplateJson));
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
