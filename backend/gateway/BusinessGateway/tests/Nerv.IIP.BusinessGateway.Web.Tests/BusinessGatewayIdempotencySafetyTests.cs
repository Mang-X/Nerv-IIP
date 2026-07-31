using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayIdempotencySafetyTests
{
    [Fact]
    public void Standard_legacy_and_body_keys_must_agree_and_resolve_to_one_normalized_value()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = " intent-001 ";
        context.Request.Headers["X-Idempotency-Key"] = "intent-001";
        var request = new RequestWithIdempotencyKey(" intent-001 ");

        var resolved = BusinessGatewayIdempotencyKey.Resolve(context, request);

        Assert.Equal("intent-001", resolved.IdempotencyKey);
    }

    [Fact]
    public void Conflicting_key_sources_fail_closed_with_a_stable_409()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = "standard-key";
        context.Request.Headers["X-Idempotency-Key"] = "legacy-key";

        var exception = Assert.Throws<BusinessServiceProxyException>(() =>
            BusinessGatewayIdempotencyKey.Resolve(
                context,
                new RequestWithIdempotencyKey("body-key")));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("idempotency-key-mismatch", exception.Message);
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("包含中文")]
    public void Invalid_key_characters_fail_closed(string key)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = key;

        var exception = Assert.Throws<BusinessServiceProxyException>(() =>
            BusinessGatewayIdempotencyKey.Resolve(
                context,
                new RequestWithIdempotencyKey(null)));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("idempotency-key-mismatch", exception.Message);
    }

    [Fact]
    public void Accepted_receipt_requires_a_relative_governed_get_readback()
    {
        var exception = Assert.Throws<BusinessServiceProxyException>(() =>
            BusinessConsoleOperationReceipts.Accepted(
                "iiot.alarm.acknowledge",
                "industrial-telemetry",
                "alarm-event",
                "alarm-001",
                "https://example.invalid/api/business-console/v1/equipment/alarms",
                "alarm-intent-001"));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    [Fact]
    public void Accepted_receipt_preserves_a_governed_rooted_application_readback()
    {
        var receipt = BusinessConsoleOperationReceipts.Accepted(
            "iiot.alarm.acknowledge",
            "industrial-telemetry",
            "alarm-event",
            "alarm-001",
            "/api/business-console/v1/equipment/alarms?organizationId=org-001",
            "alarm-intent-001");

        Assert.Equal(HttpMethod.Get.Method, receipt.ReadbackMethod);
        Assert.Equal(
            "/api/business-console/v1/equipment/alarms?organizationId=org-001",
            receipt.ReadbackPath);
    }

    [Fact]
    public async Task Downstream_idempotency_conflict_is_preserved_as_a_stable_409()
    {
        using var httpClient = Client(
            """{"success":false,"message":"idempotency-conflict","code":409}""",
            enveloped: false,
            HttpStatusCode.Conflict);
        var client = new HttpBusinessMaintenanceClient(httpClient);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateWorkOrderAsync(
                "internal-token",
                new BusinessConsoleCreateMaintenanceWorkOrderRequest(
                    "org-001",
                    "env-dev",
                    "device-001",
                    "high",
                    null,
                    "operator-001",
                    "maintenance-intent-001"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("idempotency-conflict", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quality_submission_rejects_malformed_authoritative_response(bool enveloped)
    {
        using var httpClient = Client("""{"inspectionRecordId":"","result":"passed","changedAtUtc":"2026-07-28T08:00:00Z"}""", enveloped);
        var client = new HttpBusinessQualityClient(httpClient);

        await AssertInvalidResponseAsync(() => client.CreateInspectionRecordFromTaskAsync(
            "internal-token",
            "inspection-task-001",
            new BusinessQualityCreateInspectionRecordFromTaskRequest(
                "inspection-task-001",
                "org-001",
                "env-dev",
                "inspector-001",
                [],
                null,
                [],
                "quality-intent-001"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Maintenance_create_rejects_malformed_authoritative_response(bool enveloped)
    {
        using var httpClient = Client("""{"workOrderId":"wo-001","status":"","changedAtUtc":"2026-07-28T08:00:00Z"}""", enveloped);
        var client = new HttpBusinessMaintenanceClient(httpClient);

        await AssertInvalidResponseAsync(() => client.CreateWorkOrderAsync(
            "internal-token",
            new BusinessConsoleCreateMaintenanceWorkOrderRequest(
                "org-001",
                "env-dev",
                "device-001",
                "high",
                null,
                "operator-001",
                "maintenance-intent-001"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Alarm_action_rejects_a_downstream_resource_that_does_not_match_the_route(bool enveloped)
    {
        using var httpClient = Client("""{"alarmEventId":"alarm-other"}""", enveloped);
        var client = new HttpBusinessIndustrialTelemetryClient(httpClient);

        await AssertInvalidResponseAsync(() => client.AcknowledgeAlarmAsync(
            "internal-token",
            "alarm-001",
            new BusinessConsoleAcknowledgeAlarmRequest(
                "org-001",
                "env-dev",
                DateTimeOffset.Parse("2026-07-28T08:00:00Z"),
                "operator-001",
                "alarm-intent-001"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mes_action_rejects_malformed_authoritative_response(bool enveloped)
    {
        using var httpClient = Client("""{"operationTaskId":"OP-OTHER","status":"in-progress","changedAtUtc":"2026-07-28T08:00:00Z"}""", enveloped);
        var client = new HttpBusinessMesClient(httpClient);

        await AssertInvalidResponseAsync(() => client.StartOperationTaskAsync(
            "internal-token",
            "OP-10",
            new BusinessConsoleMesOperationTaskActionRequest(
                "OP-10",
                "org-001",
                "env-dev",
                null,
                "mes-action-intent-001",
                "organization",
                "org-001"),
            CancellationToken.None));
    }

    private static HttpClient Client(
        string rawBody,
        bool enveloped,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var body = enveloped
            ? $$"""{"success":true,"data":{{rawBody}},"message":"","code":0}"""
            : rawBody;
        return new HttpClient(new StaticResponseHandler(body, statusCode))
        {
            BaseAddress = new Uri("http://downstream.local"),
        };
    }

    private static async Task AssertInvalidResponseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(action);
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    private sealed record RequestWithIdempotencyKey(string? IdempotencyKey);

    private sealed class StaticResponseHandler(string body, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
