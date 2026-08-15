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

    [Fact]
    public async Task Gateway_client_forwards_exact_device_references_in_internal_post_body()
    {
        var handler = new RecordingListWireHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        await client.ListWorkOrdersAsync(
            "test-internal-token",
            new BusinessConsoleMaintenanceWorkOrderListRequest(
                "org-001", "env-dev", DeviceAssetReferences: ["DEV,A", "DEV-B"]),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/business/internal/v1/maintenance/work-orders/query", handler.Path);
        Assert.Equal(["DEV,A", "DEV-B"], handler.Body.GetProperty("deviceAssetReferences").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-internal-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task Gateway_client_forwards_200_canonical_devices_as_400_wire_safe_aliases()
    {
        var handler = new RecordingListWireHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);
        var deviceIds = Enumerable.Range(0, 200).Select(_ => Guid.CreateVersion7().ToString()).ToArray();
        var aliases = deviceIds.SelectMany((id, index) => new[] { id, $"DEVICE-{index:000}" }).ToArray();

        await client.ListWorkOrdersAsync(
            "test-internal-token",
            new BusinessConsoleMaintenanceWorkOrderListRequest(
                "org-001", "env-dev", DeviceAssetReferences: aliases),
            CancellationToken.None);

        var values = handler.Body.GetProperty("deviceAssetReferences")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/business/internal/v1/maintenance/work-orders/query", handler.Path);
        Assert.Equal(400, values.Length);
        Assert.Equal(aliases, values);
    }

    [Fact]
    public async Task Gateway_client_keeps_small_scalar_filter_on_public_get_contract()
    {
        var handler = new RecordingListWireHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://maintenance.local") };
        var client = new HttpBusinessMaintenanceClient(httpClient);

        await client.ListWorkOrdersAsync(
            "test-internal-token",
            new BusinessConsoleMaintenanceWorkOrderListRequest(
                "org-001", "env-dev", DeviceAssetId: "DEVICE-001"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/api/business/v1/maintenance/work-orders", handler.Path);
        Assert.Contains("deviceAssetId=DEVICE-001", handler.Query, StringComparison.Ordinal);
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

    private sealed class RecordingListWireHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Path { get; private set; } = string.Empty;
        public string Query { get; private set; } = string.Empty;
        public JsonElement Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Query = request.RequestUri!.Query;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            if (request.Content is not null)
            {
                Body = JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        items = Array.Empty<object>(),
                        skip = 0,
                        take = 100,
                        total = 0,
                    },
                }),
            };
        }
    }
}
