using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// #4128 / ADR 0032 §2: ETR updates change estimates, never the actual work-order state.
public sealed class MaintenanceExpectedRestoreWireTests
{
    [Theory]
    [InlineData("Open")]
    [InlineData("Accepted")]
    [InlineData("InProgress")]
    [InlineData("Paused")]
    [InlineData("WaitingForParts")]
    public async Task Estimate_update_and_clear_keep_the_authoritative_state_and_forward_scope_and_actor(string status)
    {
        var id = "019f0000-0000-7000-8000-000000000111";
        var expected = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var handler = new EstimateHandler(id, status, expected);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(http);
        foreach (var value in new DateTimeOffset?[] { expected, null })
        {
            var json = JsonSerializer.Serialize(new { organizationId = "org-001", environmentId = "env-dev",
                action = 9, reason = "new estimate", idempotencyKey = "estimate", expectedVersion = 2,
                scopeKind = "organization", scopeId = "org-001", expectedRestoreAtUtc = value });
            var request = JsonSerializer.Deserialize<BusinessConsoleTransitionMaintenanceWorkOrderRequest>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var result = await client.TransitionWorkOrderAsync("internal-token", id, request, "tech-001", default);
            Assert.Equal(status, result.Status);
            Assert.Equal(3, result.Version);
            Assert.Equal("\"updateExpectedRestore\"", handler.LastBody.GetProperty("action").GetRawText());
            Assert.Equal("tech-001", handler.LastBody.GetProperty("actorPrincipalId").GetString());
            Assert.Equal("org-001", handler.LastBody.GetProperty("organizationId").GetString());
            Assert.Equal("env-dev", handler.LastBody.GetProperty("environmentId").GetString());
            var actual = handler.LastBody.GetProperty("expectedRestoreAtUtc");
            if (value is null) Assert.Equal(JsonValueKind.Null, actual.ValueKind);
            else Assert.Equal(value, actual.GetDateTimeOffset());
        }
        var detail = await client.GetWorkOrderAsync("internal-token", id, new("org-001", "env-dev"), default);
        Assert.Equal(expected, JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("expectedRestoreAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Estimate_replay_returns_the_original_receipt_after_a_later_state_transition()
    {
        var id = "019f0000-0000-7000-8000-000000000111";
        var expected = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var handler = new EstimateHandler(id, "InProgress", expected, currentVersion: 4, resultStatus: "Open");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(http);
        var result = await client.TransitionWorkOrderAsync("internal-token", id,
            new("org-001", "env-dev", BusinessConsoleMaintenanceWorkOrderAction.UpdateExpectedRestore,
                "new estimate", "estimate", 2, "organization", "org-001", ExpectedRestoreAtUtc: expected),
            "tech-001", default);
        Assert.Equal("Open", result.Status);
        Assert.Equal(3, result.Version);
    }

    private sealed class EstimateHandler(string id, string status, DateTimeOffset expected, int currentVersion = 2, string? resultStatus = null) : HttpMessageHandler
    {
        public JsonElement LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("internal-token", request.Headers.Authorization!.Parameter);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Contains("organizationId=org-001", request.RequestUri!.Query);
                Assert.Contains("environmentId=env-dev", request.RequestUri.Query);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = new {
                    workOrder = new { workOrderId = id, deviceAssetId = "device", priority = "high", status,
                        openedAtUtc = "2026-10-07T01:00:00Z", version = currentVersion, expectedRestoreAtUtc = expected },
                    lifecycle = Array.Empty<object>(), allowedActions = new[] { "updateExpectedRestore" }, blockReasons = Array.Empty<string>() } }) };
            }
            Assert.EndsWith($"/work-orders/{id}/actions", request.RequestUri!.AbsolutePath);
            LastBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = new {
                workOrderId = id, status = resultStatus ?? status, version = 3, changedAtUtc = "2026-10-07T02:00:00Z" } }) };
        }
    }
}
