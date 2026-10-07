using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class SchedulingWorkingDraftFacadeTests
{
    [Fact]
    public async Task Discovery_forwards_authorized_user_instead_of_client_supplied_identity_and_preserves_state()
    {
        var handler = new RecordingHandler();
        await using var lease = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(new HttpBusinessSchedulingClient(new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.test") }));
        });
        var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
        client.DefaultRequestHeaders.Add(SchedulingWorkingDraftHeaders.UserId, "victim");
        var response = await client.GetAsync("/api/business-console/v1/scheduling/working-drafts?organizationId=org-001&environmentId=env-dev&userId=victim");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user-admin", handler.UserId);
        Assert.DoesNotContain("userId", handler.Uri!.Query);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("plan-1", body.RootElement.GetProperty("data")[0].GetProperty("planId").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("data")[0].GetProperty("state").GetProperty("contractVersion").GetInt32());
    }

    [Fact]
    public async Task Save_and_clear_use_manage_permission_and_authorized_identity()
    {
        var handler = new RecordingHandler();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(new HttpBusinessSchedulingClient(new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.test") }));
        });
        var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
        client.DefaultRequestHeaders.Add(SchedulingWorkingDraftHeaders.UserId, "victim");
        var state = new SchedulingWorkingDraftStateContract(1, [new("order-1", 5, true, true)], [], []);
        var saved = await client.PutAsJsonAsync("/api/business-console/v1/scheduling/plans/plan-1/working-draft",
            new { organizationId = "org-001", environmentId = "env-dev", state, userId = "victim" }, SchedulingJson.Options);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("user-admin", handler.UserId);
        Assert.Equal(BusinessGatewayPermissions.SchedulingPlansManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal(5, JsonDocument.Parse(handler.Body!).RootElement.GetProperty("state").GetProperty("orders")[0].GetProperty("priority").GetInt32());
        Assert.DoesNotContain("userId", handler.Body!);
        var cleared = await client.DeleteAsync("/api/business-console/v1/scheduling/plans/plan-1/working-draft?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal("user-admin", handler.UserId);
        Assert.True(JsonDocument.Parse(await cleared.Content.ReadAsStringAsync()).RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(BusinessGatewayPermissions.SchedulingPlansManage, auth.LastRequirement!.PermissionCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Read_permission_does_not_allow_mutating_drafts(string method)
    {
        var handler = new RecordingHandler();
        await using var lease = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.SchedulingPlansRead), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(new HttpBusinessSchedulingClient(new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.test") }));
        });
        var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/business-console/v1/scheduling/plans/plan-1/working-draft?organizationId=org-001&environmentId=env-dev")
        { Content = JsonContent.Create(new { organizationId = "org-001", environmentId = "env-dev", state = new { contractVersion = 1, orders = Array.Empty<object>(), tasks = Array.Empty<object>(), pendingOperations = Array.Empty<object>() } }) };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(handler.Uri);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? UserId { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            UserId = request.Headers.GetValues(SchedulingWorkingDraftHeaders.UserId).Single();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var draft = new { planId = "plan-1", savedAtUtc = "2026-10-07T00:00:00Z", state = new { contractVersion = 1, orders = Array.Empty<object>(), tasks = Array.Empty<object>(), pendingOperations = Array.Empty<object>() } };
            var content = request.Method == HttpMethod.Delete
                ? JsonContent.Create(new { success = true, code = 0, message = "", errorData = Array.Empty<object>() })
                : request.Method == HttpMethod.Put
                    ? JsonContent.Create(new { success = true, data = draft })
                    : JsonContent.Create(new { success = true, data = new[] { draft } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
