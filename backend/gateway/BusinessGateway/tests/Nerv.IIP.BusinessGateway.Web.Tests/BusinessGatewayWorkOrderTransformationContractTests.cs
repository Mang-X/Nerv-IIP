using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayWorkOrderTransformationContractTests
{
    [Fact]
    public async Task Http_client_maps_strong_id_wire_and_preserves_downstream_paths_and_body()
    {
        var handler = new RecordingTransformationHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return JsonResponse(new
                {
                    success = true,
                    data = new
                    {
                        transformationId = new { id = "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8" },
                        type = "Split",
                        sourceWorkOrderIds = new[] { "WO-PARENT-001" },
                        targetWorkOrderIds = new[] { "WO-CHILD-001", "WO-CHILD-002" },
                        isIdempotentReplay = false,
                    },
                });
            }

            return JsonResponse(new
            {
                success = true,
                data = new
                {
                    transformationId = new { id = "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8" },
                    type = "Split",
                    idempotencyKey = "split-001",
                    actor = "user:planner-001",
                    reason = "按客户批次拆分",
                    occurredAtUtc = "2026-08-26T01:02:03Z",
                    lines = new[]
                    {
                        new
                        {
                            sourceWorkOrderId = "WO-PARENT-001",
                            targetWorkOrderId = "WO-CHILD-001",
                            quantity = 4m,
                            uomCode = "PCS",
                            sourceStatus = "Split",
                            targetStatus = "Created",
                            sourceVersion = 3L,
                            targetVersion = 1L,
                        },
                    },
                },
            });
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesWorkOrderTransformationClient(httpClient);
        var request = new BusinessConsoleMesSplitWorkOrderRequest(
            "WO-PARENT-001",
            "org-001",
            "env-dev",
            [
                new BusinessConsoleMesWorkOrderTransformationTargetRequest("WO-CHILD-001", 4m),
                new BusinessConsoleMesWorkOrderTransformationTargetRequest("WO-CHILD-002", 6m),
            ],
            "按客户批次拆分",
            "split-001");

        var result = await client.SplitAsync("internal-token", request, CancellationToken.None);
        var readback = await client.GetReadbackAsync(
            "internal-token",
            new BusinessConsoleMesWorkOrderTransformationReadbackRequest(
                "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8",
                "org-001",
                "env-dev"),
            CancellationToken.None);

        Assert.Equal("018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8", result.TransformationId);
        Assert.Equal("split", result.Type);
        Assert.Equal("/api/business/v1/mes/work-orders/WO-PARENT-001/split", handler.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("/api/business/v1/mes/work-order-transformations/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8?organizationId=org-001&environmentId=env-dev", handler.Requests[1].RequestUri!.PathAndQuery);
        Assert.Contains("\"organizationId\":\"org-001\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"targets\":[{\"workOrderId\":\"WO-CHILD-001\",\"quantity\":4}", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Equal("split-001", handler.Requests[0].Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("split-001", readback.IdempotencyKey);
        Assert.Equal("PCS", readback.Lines.Single().UomCode);
    }

    [Fact]
    public async Task Http_client_preserves_downstream_transformation_conflict()
    {
        var handler = new RecordingTransformationHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                success = false,
                message = "idempotency-conflict",
                code = 409,
            }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesWorkOrderTransformationClient(httpClient);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.MergeAsync(
                "internal-token",
                new BusinessConsoleMesMergeWorkOrdersRequest(
                    "org-001",
                    "env-dev",
                    ["WO-SOURCE-001", "WO-SOURCE-002"],
                    "WO-TARGET-001",
                    "按客户批次合并",
                    "merge-001"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("idempotency-conflict", exception.Message);
    }

    [Fact]
    public async Task Http_client_rejects_malformed_transformation_response()
    {
        var handler = new RecordingTransformationHandler(_ => JsonResponse(new
        {
            success = true,
            data = new
            {
                transformationId = new { },
                type = "split",
                sourceWorkOrderIds = new[] { "WO-PARENT-001" },
                targetWorkOrderIds = new[] { "WO-CHILD-001" },
                isIdempotentReplay = false,
            },
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesWorkOrderTransformationClient(httpClient);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.SplitAsync(
                "internal-token",
                new BusinessConsoleMesSplitWorkOrderRequest(
                    "WO-PARENT-001",
                    "org-001",
                    "env-dev",
                    [new BusinessConsoleMesWorkOrderTransformationTargetRequest("WO-CHILD-001", 4m)],
                    "按客户批次拆分",
                    "split-001"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("downstream-invalid-response", exception.Message);
    }

    [Fact]
    public async Task Split_facade_requires_realtime_permission_scope_and_returns_accepted_readback_contract()
    {
        var transformation = new RecordingTransformationClient();
        var mes = new RecordingMesClient();
        var masterData = new RecordingMasterDataClient();
        var auth = AuthorizationFor(BusinessGatewayPermissions.MesWorkOrdersManage);
        await using var lease = BusinessGatewayTestHost.Lease(
            auth,
            services =>
            {
                services.RemoveAll<IBusinessMesWorkOrderTransformationClient>();
                services.AddSingleton<IBusinessMesWorkOrderTransformationClient>(transformation);
                services.RemoveAll<IBusinessMesClient>();
                services.AddSingleton<IBusinessMesClient>(mes);
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(masterData);
            });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/work-orders/WO-PARENT-001/split?organizationId=org-001&environmentId=env-dev",
            new
            {
                targets = new[]
                {
                    new { workOrderId = "WO-CHILD-001", quantity = 4m },
                    new { workOrderId = "WO-CHILD-002", quantity = 6m },
                },
                reason = "按客户批次拆分",
                idempotencyKey = "split-001",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("accepted").GetBoolean());
        Assert.Equal("018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8", data.GetProperty("transformationId").GetString());
        var receipt = data.GetProperty("operationReceipt");
        Assert.Equal("accepted", receipt.GetProperty("outcome").GetString());
        Assert.False(receipt.GetProperty("stateConfirmed").GetBoolean());
        Assert.True(receipt.GetProperty("readbackRequired").GetBoolean());
        Assert.Equal("GET", receipt.GetProperty("readbackMethod").GetString());
        Assert.Equal(
            "/api/business-console/v1/mes/work-order-transformations/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8?organizationId=org-001&environmentId=env-dev",
            receipt.GetProperty("readbackPath").GetString());
        Assert.Equal(BusinessGatewayPermissions.MesWorkOrdersManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("local-internal-service-token", transformation.LastInternalToken);
        Assert.Equal("WO-PARENT-001", transformation.LastSplitRequest!.WorkOrderId);
    }

    [Fact]
    public async Task Merge_facade_checks_every_source_scope_and_returns_accepted_readback_contract()
    {
        var transformation = new RecordingTransformationClient();
        var mes = new RecordingMesClient();
        var masterData = new RecordingMasterDataClient();
        var auth = AuthorizationFor(BusinessGatewayPermissions.MesWorkOrdersManage);
        await using var lease = BusinessGatewayTestHost.Lease(
            auth,
            services =>
            {
                services.RemoveAll<IBusinessMesWorkOrderTransformationClient>();
                services.AddSingleton<IBusinessMesWorkOrderTransformationClient>(transformation);
                services.RemoveAll<IBusinessMesClient>();
                services.AddSingleton<IBusinessMesClient>(mes);
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(masterData);
            });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/work-orders/merge?organizationId=org-001&environmentId=env-dev",
            new
            {
                sourceWorkOrderIds = new[] { "WO-SOURCE-001", "WO-SOURCE-002" },
                targetWorkOrderId = "WO-TARGET-001",
                reason = "按客户批次合并",
                idempotencyKey = "merge-001",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("accepted").GetBoolean());
        Assert.Equal("merge", data.GetProperty("type").GetString());
        Assert.Equal("018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9", data.GetProperty("transformationId").GetString());
        Assert.Equal(
            "/api/business-console/v1/mes/work-order-transformations/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9?organizationId=org-001&environmentId=env-dev",
            data.GetProperty("operationReceipt").GetProperty("readbackPath").GetString());
        Assert.Equal(BusinessGatewayPermissions.MesWorkOrdersManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal(2, mes.WorkOrderListCallCount);
        Assert.Equal(1, transformation.MergeCallCount);
        Assert.Equal("WO-TARGET-001", transformation.LastMergeRequest!.TargetWorkOrderId);
        Assert.Equal("local-internal-service-token", transformation.LastInternalToken);
    }

    [Fact]
    public async Task Transformation_facade_does_not_call_mes_when_permission_is_denied()
    {
        var transformation = new RecordingTransformationClient();
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Forbidden(),
            services =>
            {
                services.RemoveAll<IBusinessMesWorkOrderTransformationClient>();
                services.AddSingleton<IBusinessMesWorkOrderTransformationClient>(transformation);
            });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/mes/work-orders/WO-PARENT-001/split?organizationId=org-001&environmentId=env-dev",
            new
            {
                targets = new[]
                {
                    new { workOrderId = "WO-CHILD-001", quantity = 4m },
                    new { workOrderId = "WO-CHILD-002", quantity = 6m },
                },
                reason = "不应转发",
                idempotencyKey = "denied-001",
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, transformation.SplitCallCount);
    }

    [Fact]
    public async Task Readback_facade_authorizes_source_scope_before_returning_line_contract()
    {
        var transformation = new RecordingTransformationClient();
        var mes = new RecordingMesClient();
        var masterData = new RecordingMasterDataClient();
        var auth = AuthorizationFor(BusinessGatewayPermissions.MesWorkOrdersRead);
        await using var lease = BusinessGatewayTestHost.Lease(
            auth,
            services =>
            {
                services.RemoveAll<IBusinessMesWorkOrderTransformationClient>();
                services.AddSingleton<IBusinessMesWorkOrderTransformationClient>(transformation);
                services.RemoveAll<IBusinessMesClient>();
                services.AddSingleton<IBusinessMesClient>(mes);
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(masterData);
            });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.GetAsync(
            "/api/business-console/v1/mes/work-order-transformations/018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8", data.GetProperty("transformationId").GetString());
        Assert.Equal("split", data.GetProperty("type").GetString());
        Assert.Equal("split-001", data.GetProperty("idempotencyKey").GetString());
        Assert.Equal("PCS", data.GetProperty("lines")[0].GetProperty("uomCode").GetString());
        Assert.Equal(1, transformation.ReadbackCallCount);
        Assert.Equal(BusinessGatewayPermissions.MesWorkOrdersRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal(1, mes.WorkOrderListCallCount);
    }

    private static FakeBusinessGatewayAuthorizationClient AuthorizationFor(string permissionCode) =>
        FakeBusinessGatewayAuthorizationClient.Allowed(
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-platform-admin",
                    "organization",
                    "org-001",
                    [permissionCode],
                    OrganizationWide: true),
            ]);

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(body),
    };

    private sealed class RecordingTransformationHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }
}

internal sealed class RecordingTransformationClient : IBusinessMesWorkOrderTransformationClient
{
    public int SplitCallCount { get; private set; }

    public int ReadbackCallCount { get; private set; }

    public int MergeCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleMesSplitWorkOrderRequest? LastSplitRequest { get; private set; }

    public BusinessConsoleMesMergeWorkOrdersRequest? LastMergeRequest { get; private set; }

    public Task<BusinessMesWorkOrderTransformationResult> SplitAsync(
        string internalBearerToken,
        BusinessConsoleMesSplitWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        SplitCallCount++;
        LastInternalToken = internalBearerToken;
        LastSplitRequest = request;
        return Task.FromResult(new BusinessMesWorkOrderTransformationResult(
            "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df8",
            "split",
            [request.WorkOrderId],
            request.Targets.Select(x => x.WorkOrderId).ToArray(),
            false));
    }

    public Task<BusinessMesWorkOrderTransformationResult> MergeAsync(
        string internalBearerToken,
        BusinessConsoleMesMergeWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        MergeCallCount++;
        LastInternalToken = internalBearerToken;
        LastMergeRequest = request;
        return Task.FromResult(new BusinessMesWorkOrderTransformationResult(
                "018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9",
                "merge",
                request.SourceWorkOrderIds,
                [request.TargetWorkOrderId],
                false));
    }

    public Task<BusinessMesWorkOrderTransformationReadback> GetReadbackAsync(
        string internalBearerToken,
        BusinessConsoleMesWorkOrderTransformationReadbackRequest request,
        CancellationToken cancellationToken) =>
        GetReadback(request);

    private Task<BusinessMesWorkOrderTransformationReadback> GetReadback(
        BusinessConsoleMesWorkOrderTransformationReadbackRequest request)
    {
        ReadbackCallCount++;
        return Task.FromResult(new BusinessMesWorkOrderTransformationReadback(
            request.TransformationId,
            "split",
            "split-001",
            "user:planner-001",
            "按客户批次拆分",
            DateTimeOffset.Parse("2026-08-26T01:02:03Z"),
            [
                new BusinessMesWorkOrderTransformationLine(
                    "WO-PARENT-001",
                    "WO-CHILD-001",
                    4m,
                    "PCS",
                    "Split",
                    "Created",
                    3,
                    1),
            ]));
    }
}
