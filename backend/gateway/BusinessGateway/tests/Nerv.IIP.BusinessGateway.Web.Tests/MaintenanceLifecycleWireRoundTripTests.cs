using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class MaintenanceLifecycleWireRoundTripTests
{
    [Fact]
    public async Task Gateway_client_writes_every_lifecycle_action_as_the_string_wire_contract()
    {
        var workOrderId = Guid.CreateVersion7().ToString();
        var handler = new RecordingWireHandler(workOrderId);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);
        var frozenWireActions = new (int NumericValue, string RawJson, string ResultStatus)[]
        {
            (0, "\"accept\"", "Accepted"),
            (1, "\"start\"", "InProgress"),
            (2, "\"pause\"", "Paused"),
            (3, "\"waitForParts\"", "WaitingForParts"),
            (4, "\"resume\"", "InProgress"),
            (5, "\"complete\"", "Completed"),
            (6, "\"verify\"", "Verified"),
            (7, "\"close\"", "Closed"),
            (8, "\"cancel\"", "Cancelled"),
        };

        foreach (var wireAction in frozenWireActions)
        {
            var action = (BusinessConsoleMaintenanceWorkOrderAction)wireAction.NumericValue;
            var response = await client.TransitionWorkOrderAsync(
                "test-internal-token",
                workOrderId,
                new BusinessConsoleTransitionMaintenanceWorkOrderRequest(
                    "org-001", "env-dev", action, "wire-round-trip", $"wire-{action}", 0, "organization", "org-001",
                    Result: action == BusinessConsoleMaintenanceWorkOrderAction.Complete ? "fixed" : null,
                    DowntimeReasonCode: action == BusinessConsoleMaintenanceWorkOrderAction.Complete ? "failure" : null,
                    DowntimeMinutes: action == BusinessConsoleMaintenanceWorkOrderAction.Complete ? 10 : null),
                "tech-001",
                CancellationToken.None);

            Assert.Equal(wireAction.ResultStatus, response.Status);
        }

        Assert.Equal(frozenWireActions.Length, handler.Requests.Count);
        Assert.Equal(
            frozenWireActions.Select(x => x.RawJson),
            handler.Requests.Select(x => x.GetProperty("action").GetRawText()));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(workOrderId, request.GetProperty("workOrderId").GetProperty("id").GetString());
            Assert.Equal("tech-001", request.GetProperty("actorPrincipalId").GetString());
        });
    }

    [Fact]
    public async Task Gateway_client_writes_assignment_work_order_id_as_the_strong_id_wire_contract()
    {
        var workOrderId = Guid.CreateVersion7().ToString();
        var handler = new RecordingAssignmentWireHandler(workOrderId);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        var response = await client.AssignWorkOrderAsync(
            "test-internal-token",
            workOrderId,
            new BusinessConsoleAssignMaintenanceWorkOrderRequest(
                "org-001", "env-dev", "tech-001", null, "dispatch", "wire-assignment", 0,
                "organization", "org-001"),
            "dispatcher-001",
            CancellationToken.None);

        Assert.Equal("Open", response.Status);
        Assert.Equal(workOrderId, handler.AssignmentRequest.GetProperty("workOrderId").GetProperty("id").GetString());
    }

    private sealed class RecordingWireHandler(string workOrderId) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/business/v1/maintenance/work-orders/{workOrderId}/actions", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-internal-token", request.Headers.Authorization?.Parameter);
            var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(body);
            var action = body.GetProperty("action").GetString();
            var status = action switch
            {
                "accept" => "Accepted",
                "start" or "resume" => "InProgress",
                "pause" => "Paused",
                "waitForParts" => "WaitingForParts",
                "complete" => "Completed",
                "verify" => "Verified",
                "close" => "Closed",
                "cancel" => "Cancelled",
                _ => throw new InvalidOperationException($"Unexpected Maintenance action '{action}'."),
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        workOrderId,
                        status,
                        changedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                        version = 1,
                    },
                }),
            };
        }
    }

    private sealed class RecordingAssignmentWireHandler(string workOrderId) : HttpMessageHandler
    {
        public JsonElement AssignmentRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        data = new
                        {
                            workOrder = new
                            {
                                workOrderId = new { id = workOrderId },
                                deviceAssetId = "DEV-001",
                                priority = "high",
                                status = "Open",
                                openedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                                version = 0,
                            },
                            lifecycle = Array.Empty<object>(),
                            allowedActions = new[] { "assign" },
                        },
                    }),
                };
            }

            AssignmentRequest = JsonSerializer.Deserialize<JsonElement>(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        workOrderId = new { id = workOrderId },
                        status = "Open",
                        changedAtUtc = DateTimeOffset.Parse("2026-08-01T00:01:00Z"),
                        version = 1,
                    },
                }),
            };
        }
    }
}
