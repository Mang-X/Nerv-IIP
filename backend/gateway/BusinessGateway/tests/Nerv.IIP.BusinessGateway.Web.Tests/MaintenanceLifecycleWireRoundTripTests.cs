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
        var actions = Enum.GetValues<BusinessConsoleMaintenanceWorkOrderAction>();

        foreach (var action in actions)
        {
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

            Assert.Equal("Accepted", response.Status);
        }

        Assert.Equal(actions.Length, handler.Requests.Count);
        Assert.Equal(
            actions.Select(x => JsonNamingPolicy.CamelCase.ConvertName(x.ToString())),
            handler.Requests.Select(x => x.GetProperty("action").GetString()));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(workOrderId, request.GetProperty("workOrderId").GetString());
            Assert.Equal("tech-001", request.GetProperty("actorPrincipalId").GetString());
        });
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
            Requests.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        workOrderId,
                        status = "Accepted",
                        changedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                        version = 1,
                    },
                }),
            };
        }
    }
}
