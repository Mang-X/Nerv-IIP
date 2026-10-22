using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract: #3653，Gateway 绑定 principal、permission-aware scope，并保持 MES 的事实与错误。
public sealed class BusinessMesAndonEndpointTests
{
    private const string Root = "/api/business-console/v1/mes/andon-calls";
    private const string Id = "01996558-4e00-7000-8000-000000000001";
    private const string Query = "organizationId=org-001&environmentId=env-dev&scopeKind=work-center&scopeId=WC-A";
    private const string Call = """
        {"id":"01996558-4e00-7000-8000-000000000001","organizationId":"org-001","environmentId":"env-dev","category":"Equipment","status":"Claimed","workOrderId":"WO-A","operationTaskId":"OP-A","workCenterId":"WC-A","callerId":"user:user-admin","raisedAtUtc":"2026-09-20T01:00:00Z","responderId":"user:user-admin","firstRespondedAtUtc":"2026-09-20T01:01:30Z","responseDurationSeconds":90,"closedAtUtc":null,"escalatedAtUtc":"2026-09-20T01:01:00Z","escalationRecipientId":"user:lead"}
        """;

    [Theory]
    [InlineData("")]
    [InlineData("/claim")]
    [InlineData("/close")]
    public async Task Mutation_binds_actor_and_scope_and_preserves_mes_facts(string action)
    {
        var downstream = new MesHandler
        {
            Payload = action switch
            {
                "" => OpenCall(),
                "/close" => Call.Replace("\"Claimed\"", "\"Closed\"").Replace("\"closedAtUtc\":null", "\"closedAtUtc\":\"2026-09-20T01:02:00Z\""),
                _ => Call,
            },
        };
        var auth = Allowed();
        await using var lease = Lease(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:forged");
        var response = await client.PostAsJsonAsync(action == "" ? Root : $"{Root}/{Id}{action}", action == "" ? Request() : ActionRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user:user-admin", downstream.Actor);
        Assert.Equal("internal-andon-token", downstream.Token);
        Assert.Equal(action == "" ? "/api/business/v1/mes/andon-calls" : $"/api/business/v1/mes/andon-calls/{Id}{action}", downstream.Path);
        using var sent = JsonDocument.Parse(downstream.Body!);
        Assert.Equal("WC-A", sent.RootElement.GetProperty("workCenterIds").GetString());
        Assert.Equal("intent-a", sent.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.False(sent.RootElement.TryGetProperty("actor", out _));
        Assert.False(sent.RootElement.TryGetProperty("callerId", out _));
        Assert.False(sent.RootElement.TryGetProperty("responderId", out _));
        Assert.Equal(BusinessGatewayPermissions.MesOperationsManage, auth.LastRequirement!.PermissionCode);
        Assert.True(auth.LastRequirement.IncludePrincipalContext);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = result.RootElement.GetProperty("data");
        if (action == "") Assert.Equal(JsonValueKind.Null, data.GetProperty("responseDurationSeconds").ValueKind);
        else Assert.Equal(90, data.GetProperty("responseDurationSeconds").GetDouble());
        Assert.Equal(action switch { "" => "open", "/close" => "closed", _ => "claimed" }, data.GetProperty("status").GetString());
        Assert.Equal("equipment", data.GetProperty("category").GetString());
        Assert.Equal("user:lead", data.GetProperty("escalationRecipientId").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reads_forward_scope_and_filters_without_recomputing_mes_total_or_duration(bool list)
    {
        var downstream = new MesHandler { Payload = list ? "{\"items\":[" + OpenCall() + "],\"total\":73}" : Call };
        var auth = Allowed();
        await using var lease = Lease(auth, downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.GetAsync((list ? Root : $"{Root}/{Id}") + "?" + Query + (list ? "&queue=AwaitingResponse&category=Equipment&workCenterId=WC-A&skip=2&take=3" : ""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("workCenterIds=WC-A", downstream.Path);
        Assert.Equal(BusinessGatewayPermissions.MesOperationsRead, auth.LastRequirement!.PermissionCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = result.RootElement.GetProperty("data");
        if (list)
        {
            Assert.Contains("queue=AwaitingResponse", downstream.Path);
            Assert.Contains("category=Equipment", downstream.Path);
            Assert.Contains("skip=2&take=3", downstream.Path);
            Assert.Equal(73, data.GetProperty("total").GetInt32());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("items")[0].GetProperty("responseDurationSeconds").ValueKind);
        }
        else Assert.Equal(Id, data.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("org-other", "env-dev", "WC-A", true)]
    [InlineData("org-001", "env-other", "WC-A", true)]
    [InlineData("org-001", "env-dev", "WC-OTHER", true)]
    [InlineData("org-001", "env-dev", "WC-A", false)]
    public async Task Unauthorized_context_or_scope_never_reaches_mes(string organization, string environment, string scopeId, bool permission)
    {
        var downstream = new MesHandler();
        await using var lease = Lease(permission ? Allowed() : FakeBusinessGatewayAuthorizationClient.Forbidden(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.PostAsJsonAsync(Root, Request(organization, environment, scopeId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    [Theory]
    [InlineData(409, "lifecycle-conflict")]
    [InlineData(409, "idempotency-conflict")]
    [InlineData(400, "仅认领人可以关闭呼叫。")]
    [InlineData(200, "未找到授权范围内的异常呼叫。")]
    public async Task Mes_business_failures_remain_failures_at_public_boundary(int status, string message)
    {
        var downstream = new MesHandler { Status = (HttpStatusCode)status, Error = message };
        await using var lease = Lease(Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.PostAsJsonAsync($"{Root}/{Id}/close", ActionRequest());
        Assert.Equal(status == 409 ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(message, result.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Openapi_publishes_five_stable_operations_and_controlled_categories()
    {
        var raw = await BusinessGatewayTestHost.GetOpenApiDocumentAsync();
        using var json = JsonDocument.Parse(raw);
        var paths = json.RootElement.GetProperty("paths");
        Assert.Equal("raiseBusinessConsoleMesAndonCall", paths.GetProperty(Root).GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("listBusinessConsoleMesAndonCalls", paths.GetProperty(Root).GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("getBusinessConsoleMesAndonCall", paths.GetProperty(Root + "/{id}").GetProperty("get").GetProperty("operationId").GetString());
        foreach (var action in new[] { "claim", "close" })
        {
            var operation = paths.GetProperty(Root + "/{id}/" + action).GetProperty("post");
            Assert.Equal(action + "BusinessConsoleMesAndonCall", operation.GetProperty("operationId").GetString());
            Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
        }
        var document = await NSwag.OpenApiDocument.FromJsonAsync(raw);
        foreach (var path in new[] { Root, Root + "/{id}/claim", Root + "/{id}/close" })
        {
            var body = document.Paths[path][NSwag.OpenApiOperationMethod.Post].RequestBody.Content["application/json"].Schema.ActualSchema;
            Assert.Contains("organizationId", body.ActualProperties.Keys);
            Assert.Contains("environmentId", body.ActualProperties.Keys);
            Assert.Contains("scopeKind", body.ActualProperties.Keys);
            Assert.Contains("scopeId", body.ActualProperties.Keys);
        }
        var schema = document.Paths[Root][NSwag.OpenApiOperationMethod.Post].RequestBody.Content["application/json"].Schema.ActualSchema;
        Assert.Equal(new[] { "materialShortage", "equipment", "quality", "process" }, schema.ActualProperties["category"].ActualSchema.Enumeration);
    }

    private static object Request(string organization = "org-001", string environment = "env-dev", string scopeId = "WC-A") => new
    {
        organizationId = organization, environmentId = environment, scopeKind = "work-center", scopeId,
        idempotencyKey = "intent-a", category = "Equipment", workOrderId = "WO-A", operationTaskId = "OP-A", workCenterId = "WC-A",
    };

    private static object ActionRequest() => new { organizationId = "org-001", environmentId = "env-dev", scopeKind = "work-center", scopeId = "WC-A", idempotencyKey = "intent-a" };

    private static string OpenCall() => Call.Replace("\"Claimed\"", "\"Open\"")
        .Replace("\"responderId\":\"user:user-admin\"", "\"responderId\":null")
        .Replace("\"firstRespondedAtUtc\":\"2026-09-20T01:01:30Z\"", "\"firstRespondedAtUtc\":null")
        .Replace("\"responseDurationSeconds\":90", "\"responseDurationSeconds\":null");

    [Theory]
    [InlineData("actor")]
    [InlineData("callerId")]
    [InlineData("responderId")]
    [InlineData("workCenterIds")]
    public async Task Client_cannot_supply_trusted_actor_or_scope_fields(string field)
    {
        var downstream = new MesHandler();
        await using var lease = Lease(Allowed(), downstream);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var payload = JsonSerializer.Serialize(Request());
        payload = payload[..^1] + ",\"" + field + "\":\"forged\"}";
        var response = await client.PostAsync(Root, new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.Calls);
    }

    private static FakeBusinessGatewayAuthorizationClient Allowed() => FakeBusinessGatewayAuthorizationClient.Allowed(
        scopeGrants: [new AuthorizationScopeGrant("membership", "membership-a", "work-center", "WC-A",
            [BusinessGatewayPermissions.MesOperationsRead, BusinessGatewayPermissions.MesOperationsManage])]);

    private static BusinessGatewayTestHostLease Lease(IBusinessGatewayAuthorizationClient auth, MesHandler downstream) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(new RecordingMasterDataClient
            {
                PrincipalWorkContext = new("ready", null, [], [], [], [], [],
                    [new BusinessMasterDataWorkContextCandidateScope("work-center", "WC-A", "加工中心", "workshop-covered", [])],
                    ["work-center"], []),
            });
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-andon-token"));
            services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder =>
            {
                if (builder.Name!.Contains("Andon", StringComparison.Ordinal)) builder.PrimaryHandler = downstream;
            }));
        }, BusinessGatewayTestHostProfile.ServiceBaseUrls);

    private sealed class MesHandler : HttpMessageHandler
    {
        public string Payload { get; init; } = Call;
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? Error { get; init; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public string? Actor { get; private set; }
        public string? Token { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri!.PathAndQuery;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Actor = request.Headers.TryGetValues("X-Authenticated-Actor", out var actors) ? actors.Single() : null;
            Token = request.Headers.Authorization!.Parameter;
            return new(Status) { Content = new StringContent(Error is null ? "{\"success\":true,\"data\":" + Payload + "}" : JsonSerializer.Serialize(new { success = false, message = Error }), Encoding.UTF8, "application/json") };
        }
    }
}
