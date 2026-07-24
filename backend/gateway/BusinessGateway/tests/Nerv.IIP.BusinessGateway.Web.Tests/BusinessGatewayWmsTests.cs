using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayWmsTests
{
    [Fact]
    public async Task Wms_http_client_forwards_write_operations_to_backend_wms_paths()
    {
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            object data = path switch
            {
                "/api/business/v1/wms/inbound-orders" => new { inboundOrderId = "inbound-order-http" },
                "/api/business/v1/wms/inbound-orders/inbound-order-001/putaway-tasks" => new { warehouseTaskId = "warehouse-task-http" },
                "/api/business/v1/wms/inbound-orders/inbound-order-001/complete" => new { requestId = "request-in-http", inventoryMovementId = "movement-in-http" },
                "/api/business/v1/wms/outbound-orders" => new { outboundOrderId = "outbound-order-http" },
                "/api/business/v1/wms/outbound-orders/outbound-order-001/picking-tasks" => new { warehouseTaskId = "warehouse-task-http" },
                "/api/business/v1/wms/outbound-orders/outbound-order-001/complete" => new { inventoryMovementId = "movement-out-http" },
                "/api/business/v1/wms/outbound-orders/outbound-order-001/inventory-posting/retry" => new { requestId = "request-out-retry-http" },
                "/api/business/v1/wms/count-executions" => new { countExecutionId = "count-execution-http" },
                "/api/business/v1/wms/count-executions/count-execution-001/complete" => new { inventoryMovementId = "movement-count-http" },
                "/api/business/v1/wms/wcs-tasks/warehouse-task-001/dispatch" => new { wcsTaskId = "wcs-task-http" },
                "/api/business/v1/wms/wcs-tasks/EXT-001/fail" => new { },
                "/api/business/v1/wms/wcs-tasks/EXT-001/complete" => new { },
                _ => throw new InvalidOperationException($"Unexpected path {path}"),
            };

            return JsonResponse(HttpStatusCode.OK, new
            {
                data,
                success = true,
                message = string.Empty,
                code = 0,
            });
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://wms.local") };
        var client = new HttpBusinessWmsClient(httpClient);

        await client.CreateInboundOrderAsync("internal-token-001", ValidInboundRequest(), CancellationToken.None);
        await client.CreatePutawayTaskAsync("internal-token-001", "inbound-order-001", ValidPutawayRequest(), CancellationToken.None);
        var completedInbound = await client.CompleteInboundOrderAsync("internal-token-001", "inbound-order-001", ValidCompleteInboundRequest(), CancellationToken.None);
        await client.CreateOutboundOrderAsync("internal-token-001", ValidOutboundRequest(), CancellationToken.None);
        await client.CreatePickingTaskAsync("internal-token-001", "outbound-order-001", ValidPickingRequest(), CancellationToken.None);
        await client.CompleteOutboundOrderAsync("internal-token-001", "outbound-order-001", ValidCompleteOutboundRequest(), CancellationToken.None);
        await client.RetryOutboundInventoryPostingAsync("internal-token-001", "outbound-order-001", ValidRetryOutboundRequest(), CancellationToken.None);
        await client.CreateCountExecutionAsync("internal-token-001", ValidCreateCountRequest(), CancellationToken.None);
        await client.CompleteCountExecutionAsync("internal-token-001", "count-execution-001", ValidCompleteCountRequest(), CancellationToken.None);
        await client.DispatchWcsTaskAsync("internal-token-001", "warehouse-task-001", ValidDispatchWcsRequest(), CancellationToken.None);
        await client.FailWcsTaskAsync("internal-token-001", "EXT-001", ValidFailWcsRequest(), CancellationToken.None);
        await client.CompleteWcsTaskAsync("internal-token-001", "EXT-001", ValidCompleteWcsRequest(), CancellationToken.None);

        Assert.Equal(
        [
            "POST /api/business/v1/wms/inbound-orders",
            "POST /api/business/v1/wms/inbound-orders/inbound-order-001/putaway-tasks",
            "POST /api/business/v1/wms/inbound-orders/inbound-order-001/complete",
            "POST /api/business/v1/wms/outbound-orders",
            "POST /api/business/v1/wms/outbound-orders/outbound-order-001/picking-tasks",
            "POST /api/business/v1/wms/outbound-orders/outbound-order-001/complete",
            "POST /api/business/v1/wms/outbound-orders/outbound-order-001/inventory-posting/retry",
            "POST /api/business/v1/wms/count-executions",
            "POST /api/business/v1/wms/count-executions/count-execution-001/complete",
            "POST /api/business/v1/wms/wcs-tasks/warehouse-task-001/dispatch",
            "POST /api/business/v1/wms/wcs-tasks/EXT-001/fail",
            "POST /api/business/v1/wms/wcs-tasks/EXT-001/complete",
        ],
        handler.Requests.Select(request => $"{request.Method} {request.RequestUri!.AbsolutePath}").ToArray());
        Assert.All(handler.Requests, request => Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter));
        Assert.Equal("request-in-http", completedInbound.RequestId);

        using var createInboundBody = JsonDocument.Parse(handler.RequestBodies[0]!);
        var createInboundLine = createInboundBody.RootElement.GetProperty("lines")[0];
        Assert.Equal("2026-01-15", createInboundLine.GetProperty("productionDate").GetString());
        Assert.Equal("2027-01-15", createInboundLine.GetProperty("expiryDate").GetString());

        using var completeInboundBody = JsonDocument.Parse(handler.RequestBodies[2]!);
        Assert.Equal("complete-in-001", completeInboundBody.RootElement.GetProperty("idempotencyKey").GetString());
        var completeInboundLine = completeInboundBody.RootElement.GetProperty("lines")[0];
        Assert.Equal("10", completeInboundLine.GetProperty("lineNo").GetString());
        Assert.Equal("LOT-CAPTURED-001", completeInboundLine.GetProperty("lotNo").GetString());
        Assert.Equal("2026-01-16", completeInboundLine.GetProperty("productionDate").GetString());
        Assert.Equal("2027-01-16", completeInboundLine.GetProperty("expiryDate").GetString());
    }

    [Fact]
    public async Task Wms_http_client_forwards_list_paging_status_and_keyword_filters_to_backend_wms_paths()
    {
        var handler = new RecordingHandler(request =>
            JsonResponse(HttpStatusCode.OK, new
            {
                data = request.RequestUri!.AbsolutePath switch
                {
                    "/api/business/v1/wms/inbound-orders" => new { items = Array.Empty<object>(), total = 23 },
                    "/api/business/v1/wms/putaway-tasks" => new { items = Array.Empty<object>(), total = 19 },
                    "/api/business/v1/wms/outbound-orders" => new
                    {
                        items = new[]
                        {
                            new
                            {
                                outboundOrderId = "outbound-order-failed-001",
                                outboundOrderNo = "DO-FAILED-001",
                                status = "InventoryPostingFailed",
                                siteCode = "finished-goods",
                                inventoryPostingStatus = "failed",
                                failureCode = "NEGATIVE_ON_HAND",
                                failureMessage = "Stock movement would make on-hand quantity negative.",
                                lines = new[]
                                {
                                    new
                                    {
                                        lineNo = "SO-LINE-001",
                                        skuCode = "SKU-FG-1000",
                                        uomCode = "kg",
                                        requestedQuantity = 4,
                                        issuedQuantity = 4,
                                        locationCode = "receiving",
                                        lotNo = "LOT-001",
                                        serialNo = (string?)null,
                                        qualityStatus = "unrestricted",
                                        ownerType = "production",
                                        ownerId = (string?)null,
                                        inventoryPostingStatus = "failed",
                                        failureCode = "NEGATIVE_ON_HAND",
                                        failureMessage = "Stock movement would make on-hand quantity negative.",
                                    },
                                },
                                createdAtUtc = "2026-06-01T09:00:00Z",
                                completedAtUtc = (string?)null,
                            },
                        },
                        total = 17,
                    },
                    "/api/business/v1/wms/picking-tasks" => new { items = Array.Empty<object>(), total = 13 },
                    "/api/business/v1/wms/count-executions" => new { items = Array.Empty<object>(), total = 11 },
                    "/api/business/v1/wms/wcs-tasks" => new { items = Array.Empty<object>(), total = 9 },
                    "/api/business/v1/wms/receiving-quality-gates" => (object)new
                    {
                        items = new[]
                        {
                            new
                            {
                                inboundOrderId = "inbound-order-001",
                                inboundOrderLineId = "inbound-order-line-001",
                                organizationId = "org-001",
                                environmentId = "env-dev",
                                inboundOrderNo = "IN-GATE-001",
                                inboundOrderStatus = "Completed",
                                siteCode = "S1",
                                lineNo = "10",
                                skuCode = "SKU-FG-1000",
                                uomCode = "kg",
                                receivedQuantity = 5,
                                stagingLocationCode = "STAGE-01",
                                lotNo = "LOT-001",
                                serialNo = (string?)null,
                                productionDate = "2026-01-15",
                                expiryDate = "2027-01-15",
                                qualityStatus = "quality",
                                qualityGateStatus = "rejected",
                                inspectionRecordId = "QI-REJ-001",
                                qualityDispositionReason = "critical-defect",
                                ownerType = "company",
                                ownerId = (string?)null,
                                createdAtUtc = "2026-06-01T10:10:00Z",
                            },
                        },
                        total = 7,
                    },
                    "/api/business/v1/wms/supplier-return-requests" => new { items = Array.Empty<object>(), total = 5 },
                    _ => throw new InvalidOperationException($"Unexpected path {request.RequestUri!.AbsolutePath}"),
                },
                success = true,
                message = string.Empty,
                code = 0,
            }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://wms.local") };
        var client = new HttpBusinessWmsClient(httpClient);

        var inbound = await client.ListInboundOrdersAsync("internal-token-001", new BusinessConsoleWmsListRequest("org-001", "env-dev", 10, 20, "Open", "IN-001"), CancellationToken.None);
        var putaway = await client.ListPutawayTasksAsync("internal-token-001", new BusinessConsoleWmsWarehouseTaskListRequest("org-001", "env-dev", "RECV-01", "user-001", 15, 25, "Open", "PUT-001"), CancellationToken.None);
        var outbound = await client.ListOutboundOrdersAsync("internal-token-001", new BusinessConsoleWmsListRequest("org-001", "env-dev", 20, 10, "Completed", "OUT-001"), CancellationToken.None);
        var picking = await client.ListPickingTasksAsync("internal-token-001", new BusinessConsoleWmsWarehouseTaskListRequest("org-001", "env-dev", "BIN-01", "user-002", 25, 35, "Open", "PICK-001"), CancellationToken.None);
        var count = await client.ListCountExecutionsAsync("internal-token-001", new BusinessConsoleWmsCountExecutionListRequest("org-001", "env-dev", "BIN-02", 5, 15, "Open", "COUNT-001"), CancellationToken.None);
        var wcs = await client.ListWcsTasksAsync("internal-token-001", new BusinessConsoleWmsWcsTaskListRequest("org-001", "env-dev", "EXT-001", "warehouse-task-001", 30, 15, "Failed", true, "EXT"), CancellationToken.None);
        var gates = await client.ListReceivingQualityGatesAsync("internal-token-001", new BusinessConsoleWmsReceivingQualityGateListRequest("org-001", "env-dev", 5, 15, "rejected", "IN-GATE", IncludeNotRequired: true, InboundOrderNo: "IN-EXACT"), CancellationToken.None);
        var returns = await client.ListSupplierReturnRequestsAsync("internal-token-001", new BusinessConsoleWmsListRequest("org-001", "env-dev", 10, 20, "Open", "RTS"), CancellationToken.None);

        Assert.Equal(23, inbound.Total);
        Assert.Equal(19, putaway.Total);
        Assert.Equal(17, outbound.Total);
        var failedOutbound = Assert.Single(outbound.Items);
        Assert.Equal("finished-goods", failedOutbound.SiteCode);
        Assert.Equal("failed", failedOutbound.InventoryPostingStatus);
        Assert.Equal("NEGATIVE_ON_HAND", failedOutbound.FailureCode);
        Assert.Equal("receiving", Assert.Single(failedOutbound.Lines).LocationCode);
        Assert.Equal(13, picking.Total);
        Assert.Equal(11, count.Total);
        Assert.Equal(9, wcs.Total);
        Assert.Equal(7, gates.Total);
        var gate = Assert.Single(gates.Items);
        Assert.Equal(new DateOnly(2026, 1, 15), gate.ProductionDate);
        Assert.Equal(new DateOnly(2027, 1, 15), gate.ExpiryDate);
        Assert.Equal(5, returns.Total);
        Assert.Equal(
        [
            "GET /api/business/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skip=10&take=20&status=Open&keyword=IN-001",
            "GET /api/business/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&locationCode=RECV-01&operatorUserId=user-001&skip=15&take=25&status=Open&keyword=PUT-001",
            "GET /api/business/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&skip=20&take=10&status=Completed&keyword=OUT-001",
            "GET /api/business/v1/wms/picking-tasks?organizationId=org-001&environmentId=env-dev&locationCode=BIN-01&operatorUserId=user-002&skip=25&take=35&status=Open&keyword=PICK-001",
            "GET /api/business/v1/wms/count-executions?organizationId=org-001&environmentId=env-dev&locationCode=BIN-02&skip=5&take=15&status=Open&keyword=COUNT-001",
            "GET /api/business/v1/wms/wcs-tasks?organizationId=org-001&environmentId=env-dev&externalTaskId=EXT-001&warehouseTaskId=warehouse-task-001&skip=30&take=15&status=Failed&failed=true&keyword=EXT",
            "GET /api/business/v1/wms/receiving-quality-gates?organizationId=org-001&environmentId=env-dev&skip=5&take=15&gateStatus=rejected&keyword=IN-GATE&includeNotRequired=true&inboundOrderNo=IN-EXACT",
            "GET /api/business/v1/wms/supplier-return-requests?organizationId=org-001&environmentId=env-dev&skip=10&take=20&status=Open&keyword=RTS",
        ],
        handler.Requests.Select(request => $"{request.Method} {request.RequestUri!.PathAndQuery}").ToArray());
    }

    [Fact]
    public async Task Receipt_write_facades_use_receipts_manage_permission_internal_token_and_route_ids()
    {
        var wms = new RecordingWmsClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var inbound = await client.PostAsJsonAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            inboundOrderNo = "IN-NEW",
            sourceDocumentType = "purchase-receipt",
            sourceDocumentId = "PR-001",
            siteCode = "S1",
            lines = new[]
            {
                new
                {
                    lineNo = "10",
                    skuCode = "SKU-001",
                    uomCode = "EA",
                    receivedQuantity = 3,
                    stagingLocationCode = "STAGE-01",
                    lotNo = "LOT-001",
                    serialNo = (string?)null,
                    productionDate = "2026-01-15",
                    expiryDate = "2027-01-15",
                    qualityStatus = "qualified",
                    ownerType = "company",
                    ownerId = (string?)null,
                },
            },
        });
        var putaway = await client.PostAsJsonAsync("/api/business-console/v1/wms/inbound-orders/inbound-order-001/putaway-tasks?organizationId=org-001&environmentId=env-dev", new
        {
            taskNo = "PUT-001",
            lineNo = "10",
            fromLocationCode = "STAGE-01",
            toLocationCode = "BIN-01",
            quantity = 3,
        });
        var completeInbound = await client.PostAsJsonAsync("/api/business-console/v1/wms/inbound-orders/inbound-order-001/complete?organizationId=org-001&environmentId=env-dev", new
        {
            idempotencyKey = "complete-in-001",
            lines = new[]
            {
                new
                {
                    lineNo = "10",
                    lotNo = "LOT-CAPTURED-001",
                    productionDate = "2026-01-16",
                    expiryDate = "2027-01-16",
                },
            },
        });
        var count = await client.PostAsJsonAsync("/api/business-console/v1/wms/count-executions?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            countNo = "COUNT-001",
            skuCode = "SKU-001",
            uomCode = "EA",
            siteCode = "S1",
            locationCode = "BIN-01",
            expectedQuantity = 9,
        });
        var completeCount = await client.PostAsJsonAsync("/api/business-console/v1/wms/count-executions/count-execution-001/complete?organizationId=org-001&environmentId=env-dev", new
        {
            countedQuantity = 8,
            idempotencyKey = "complete-count-001",
        });

        Assert.Equal(HttpStatusCode.OK, inbound.StatusCode);
        Assert.Equal(HttpStatusCode.OK, putaway.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completeInbound.StatusCode);
        Assert.Equal(HttpStatusCode.OK, count.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completeCount.StatusCode);
        var completeInboundResponse = await completeInbound.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("request-in-001", completeInboundResponse.GetProperty("data").GetProperty("requestId").GetString());
        Assert.All(auth.Requirements, requirement => Assert.Equal(BusinessGatewayPermissions.WmsReceiptsManage, requirement.PermissionCode));
        Assert.Equal(["create-inbound", "create-putaway", "complete-inbound", "create-count", "complete-count"], wms.Calls);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal("IN-NEW", wms.LastCreateInboundRequest!.InboundOrderNo);
        var createInboundLine = Assert.Single(wms.LastCreateInboundRequest.Lines);
        Assert.Equal(new DateOnly(2026, 1, 15), createInboundLine.ProductionDate);
        Assert.Equal(new DateOnly(2027, 1, 15), createInboundLine.ExpiryDate);
        Assert.Equal("inbound-order-001", wms.LastCreatePutawayRequest!.InboundOrderId);
        Assert.Equal("inbound-order-001", wms.LastCompleteInboundRequest!.InboundOrderId);
        Assert.Equal("complete-in-001", wms.LastCompleteInboundRequest.IdempotencyKey);
        var completeInboundLine = Assert.Single(wms.LastCompleteInboundRequest.Lines!);
        Assert.Equal("10", completeInboundLine.LineNo);
        Assert.Equal("LOT-CAPTURED-001", completeInboundLine.LotNo);
        Assert.Equal(new DateOnly(2026, 1, 16), completeInboundLine.ProductionDate);
        Assert.Equal(new DateOnly(2027, 1, 16), completeInboundLine.ExpiryDate);
        Assert.Equal("COUNT-001", wms.LastCreateCountRequest!.CountNo);
        Assert.Equal("count-execution-001", wms.LastCompleteCountRequest!.CountExecutionId);
    }

    [Fact]
    public async Task Shipment_write_facades_use_shipments_manage_permission_internal_token_and_route_ids()
    {
        var wms = new RecordingWmsClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var outbound = await client.PostAsJsonAsync("/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            outboundOrderNo = "OUT-NEW",
            sourceDocumentType = "sales-shipment",
            sourceDocumentId = "SO-001",
            siteCode = "S1",
            lines = new[]
            {
                new
                {
                    lineNo = "10",
                    skuCode = "SKU-001",
                    uomCode = "EA",
                    requestedQuantity = 2,
                    pickLocationCode = "BIN-01",
                    lotNo = "LOT-001",
                    serialNo = (string?)null,
                    qualityStatus = "qualified",
                    ownerType = "company",
                    ownerId = (string?)null,
                },
            },
        });
        var picking = await client.PostAsJsonAsync("/api/business-console/v1/wms/outbound-orders/outbound-order-001/picking-tasks?organizationId=org-001&environmentId=env-dev", new
        {
            taskNo = "PICK-001",
            lineNo = "10",
            fromLocationCode = "BIN-01",
            toLocationCode = "SHIP-01",
            quantity = 2,
        });
        var completeOutbound = await client.PostAsJsonAsync("/api/business-console/v1/wms/outbound-orders/outbound-order-001/complete?organizationId=org-001&environmentId=env-dev", new
        {
            packReviewNo = "PACK-001",
            passed = true,
            idempotencyKey = "complete-out-001",
        });
        var retryOutbound = await client.PostAsJsonAsync("/api/business-console/v1/wms/outbound-orders/outbound-order-001/inventory-posting/retry?organizationId=org-001&environmentId=env-dev", new
        {
            idempotencyKey = "retry-out-001",
        });

        Assert.Equal(HttpStatusCode.OK, outbound.StatusCode);
        Assert.Equal(HttpStatusCode.OK, picking.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completeOutbound.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryOutbound.StatusCode);
        Assert.All(auth.Requirements, requirement => Assert.Equal(BusinessGatewayPermissions.WmsShipmentsManage, requirement.PermissionCode));
        Assert.Equal(["create-outbound", "create-picking", "complete-outbound", "retry-outbound"], wms.Calls);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal("OUT-NEW", wms.LastCreateOutboundRequest!.OutboundOrderNo);
        Assert.Equal("outbound-order-001", wms.LastCreatePickingRequest!.OutboundOrderId);
        Assert.Equal("outbound-order-001", wms.LastCompleteOutboundRequest!.OutboundOrderId);
        Assert.Equal("outbound-order-001", wms.LastRetryOutboundRequest!.OutboundOrderId);
        Assert.Equal("retry-out-001", wms.LastRetryOutboundRequest.IdempotencyKey);
    }

    [Fact]
    public async Task Wcs_write_facades_use_automation_permission_internal_token_and_route_ids()
    {
        var wms = new RecordingWmsClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var dispatch = await client.PostAsJsonAsync("/api/business-console/v1/wms/wcs-tasks/warehouse-task-001/dispatch?organizationId=org-001&environmentId=env-dev", new
        {
            adapterType = "agv",
            externalTaskId = "EXT-001",
            payloadJson = "{}",
        });
        var fail = await client.PostAsJsonAsync("/api/business-console/v1/wms/wcs-tasks/EXT-001/fail?organizationId=org-001&environmentId=env-dev", new
        {
            failureCode = "PLC_TIMEOUT",
            failureMessage = "PLC did not acknowledge.",
        });
        var complete = await client.PostAsJsonAsync("/api/business-console/v1/wms/wcs-tasks/EXT-001/complete?organizationId=org-001&environmentId=env-dev", new
        {
            completionPayloadJson = "{}",
        });

        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.All(auth.Requirements, requirement => Assert.Equal(BusinessGatewayPermissions.WmsAutomationManage, requirement.PermissionCode));
        Assert.Equal(["dispatch-wcs", "fail-wcs", "complete-wcs"], wms.Calls);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal("warehouse-task-001", wms.LastDispatchWcsRequest!.WarehouseTaskId);
        Assert.Equal("EXT-001", wms.LastFailWcsRequest!.ExternalTaskId);
        Assert.Equal("EXT-001", wms.LastCompleteWcsRequest!.ExternalTaskId);
    }

    [Fact]
    public async Task Inbound_orders_include_inventory_context_in_single_facade_response()
    {
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1&skip=10&take=20&status=Open&keyword=IN");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal("internal-test-token", inventory.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsListRequest("org-001", "env-dev", 10, 20, "Open", "IN"), wms.LastInboundRequest);
        Assert.Equal("SKU-001", inventory.LastAvailabilityRequest!.SkuCode);
        Assert.Equal("S1", inventory.LastAvailabilityRequest.SiteCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(47, data.GetProperty("total").GetInt32());
        Assert.Equal("available", data.GetProperty("sourceStatus").GetString());
        Assert.Equal("BusinessInventory", data.GetProperty("inventoryContext").GetProperty("source").GetString());
        Assert.Equal(8, data.GetProperty("inventoryContext").GetProperty("availableQuantity").GetDecimal());
        Assert.Equal("IN-001", data.GetProperty("items")[0].GetProperty("inboundOrderNo").GetString());
    }

    [Fact]
    public async Task Inbound_orders_return_scope_required_inventory_context_when_inventory_scope_is_missing()
    {
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, inventory.AvailabilityCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("scope-required", data.GetProperty("sourceStatus").GetString());
        var context = data.GetProperty("inventoryContext");
        Assert.Equal("scope-required", context.GetProperty("status").GetString());
        Assert.Equal("sku-uom-site-required", context.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Inbound_orders_return_forbidden_inventory_context_when_inventory_permission_is_denied()
    {
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(
            FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.WmsReceiptsRead),
            services =>
            {
                services.RemoveAll<IBusinessWmsClient>();
                services.AddSingleton<IBusinessWmsClient>(wms);
                services.RemoveAll<IBusinessInventoryClient>();
                services.AddSingleton<IBusinessInventoryClient>(inventory);
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
            });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, inventory.AvailabilityCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var context = document.RootElement.GetProperty("data").GetProperty("inventoryContext");
        Assert.Equal("forbidden", context.GetProperty("status").GetString());
        Assert.Equal("forbidden", context.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("proxy")]
    [InlineData("http")]
    public async Task Inbound_orders_return_unavailable_inventory_context_when_inventory_source_fails(string failureKind)
    {
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient
        {
            AvailabilityFailure = failureKind == "proxy"
                ? BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "inventory-unavailable")
                : new HttpRequestException("connection refused"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inventory.AvailabilityCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var context = document.RootElement.GetProperty("data").GetProperty("inventoryContext");
        Assert.Equal("unavailable", context.GetProperty("status").GetString());
        Assert.Equal(failureKind == "proxy" ? "downstream-request-failed" : "downstream-unavailable", context.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Outbound_orders_use_shipments_permission_and_internal_service_token()
    {
        var wms = new RecordingWmsClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&skip=20&take=10&status=Completed&keyword=OUT");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsListRequest("org-001", "env-dev", 20, 10, "Completed", "OUT"), wms.LastOutboundRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(31, data.GetProperty("total").GetInt32());
        Assert.Equal("OUT-001", data.GetProperty("items")[0].GetProperty("outboundOrderNo").GetString());
    }

    [Fact]
    public async Task Wms_task_and_count_lists_use_read_permissions_internal_token_and_filters()
    {
        var wms = new RecordingWmsClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var putaway = await client.GetAsync("/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&locationCode=RECV-01&operatorUserId=user-001&skip=10&take=20&status=Open&keyword=PUT");
        var picking = await client.GetAsync("/api/business-console/v1/wms/picking-tasks?organizationId=org-001&environmentId=env-dev&locationCode=BIN-01&operatorUserId=user-002&skip=20&take=10&status=Open&keyword=PICK");
        var count = await client.GetAsync("/api/business-console/v1/wms/count-executions?organizationId=org-001&environmentId=env-dev&locationCode=BIN-02&skip=5&take=15&status=Open&keyword=COUNT");

        Assert.Equal(HttpStatusCode.OK, putaway.StatusCode);
        Assert.Equal(HttpStatusCode.OK, picking.StatusCode);
        Assert.Equal(HttpStatusCode.OK, count.StatusCode);
        Assert.Equal(
        [
            BusinessGatewayPermissions.WmsReceiptsRead,
            BusinessGatewayPermissions.WmsShipmentsRead,
            BusinessGatewayPermissions.WmsReceiptsRead,
        ],
        auth.Requirements.Select(requirement => requirement.PermissionCode).ToArray());
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsWarehouseTaskListRequest("org-001", "env-dev", "RECV-01", "user-001", 10, 20, "Open", "PUT"), wms.LastPutawayTaskRequest);
        Assert.Equal(new BusinessConsoleWmsWarehouseTaskListRequest("org-001", "env-dev", "BIN-01", "user-002", 20, 10, "Open", "PICK"), wms.LastPickingTaskRequest);
        Assert.Equal(new BusinessConsoleWmsCountExecutionListRequest("org-001", "env-dev", "BIN-02", 5, 15, "Open", "COUNT"), wms.LastCountExecutionListRequest);

        using var putawayDocument = JsonDocument.Parse(await putaway.Content.ReadAsStringAsync());
        using var pickingDocument = JsonDocument.Parse(await picking.Content.ReadAsStringAsync());
        using var countDocument = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
        Assert.Equal("PUT-001", putawayDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("taskNo").GetString());
        Assert.Equal("PICK-001", pickingDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("taskNo").GetString());
        Assert.Equal("COUNT-001", countDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("countNo").GetString());
    }

    [Fact]
    public async Task Wcs_tasks_use_automation_permission_and_filters()
    {
        var wms = new RecordingWmsClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/wcs-tasks?organizationId=org-001&environmentId=env-dev&externalTaskId=EXT-001&warehouseTaskId=warehouse-task-001&skip=30&take=15&status=Failed&failed=true&keyword=EXT");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsWcsTaskListRequest("org-001", "env-dev", "EXT-001", "warehouse-task-001", 30, 15, "Failed", true, "EXT"), wms.LastWcsTaskRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(14, data.GetProperty("total").GetInt32());
        Assert.Equal("EXT-001", data.GetProperty("items")[0].GetProperty("externalTaskId").GetString());
    }

    [Fact]
    public async Task Receiving_quality_gate_and_supplier_return_lists_use_receipts_read_permission_internal_token_and_filters()
    {
        var wms = new RecordingWmsClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var gates = await client.GetAsync("/api/business-console/v1/wms/receiving-quality-gates?organizationId=org-001&environmentId=env-dev&skip=5&take=15&gateStatus=rejected&keyword=IN-GATE");
        var returns = await client.GetAsync("/api/business-console/v1/wms/supplier-return-requests?organizationId=org-001&environmentId=env-dev&skip=10&take=20&status=Open&keyword=RTS");

        Assert.Equal(HttpStatusCode.OK, gates.StatusCode);
        Assert.Equal(HttpStatusCode.OK, returns.StatusCode);
        Assert.Equal(
        [
            BusinessGatewayPermissions.WmsReceiptsRead,
            BusinessGatewayPermissions.WmsReceiptsRead,
        ],
        auth.Requirements.Select(requirement => requirement.PermissionCode).ToArray());
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal(new BusinessConsoleWmsReceivingQualityGateListRequest("org-001", "env-dev", 5, 15, "rejected", "IN-GATE"), wms.LastReceivingQualityGateRequest);
        Assert.Equal(new BusinessConsoleWmsListRequest("org-001", "env-dev", 10, 20, "Open", "RTS"), wms.LastSupplierReturnRequest);

        using var gatesDocument = JsonDocument.Parse(await gates.Content.ReadAsStringAsync());
        var gatesData = gatesDocument.RootElement.GetProperty("data");
        Assert.Equal(41, gatesData.GetProperty("total").GetInt32());
        var gateItem = gatesData.GetProperty("items")[0];
        Assert.Equal("IN-GATE-001", gateItem.GetProperty("inboundOrderNo").GetString());
        Assert.Equal("rejected", gateItem.GetProperty("qualityGateStatus").GetString());
        Assert.Equal("QI-REJ-001", gateItem.GetProperty("inspectionRecordId").GetString());
        Assert.Equal("critical-defect", gateItem.GetProperty("qualityDispositionReason").GetString());
        Assert.Equal("2026-01-15", gateItem.GetProperty("productionDate").GetString());
        Assert.Equal("2027-01-15", gateItem.GetProperty("expiryDate").GetString());

        using var returnsDocument = JsonDocument.Parse(await returns.Content.ReadAsStringAsync());
        var returnsData = returnsDocument.RootElement.GetProperty("data");
        Assert.Equal(37, returnsData.GetProperty("total").GetInt32());
        var returnItem = returnsData.GetProperty("items")[0];
        Assert.Equal("RTS-IN-GATE-001-10-QI-REJ-001", returnItem.GetProperty("supplierReturnNo").GetString());
        Assert.Equal("return-to-supplier", returnItem.GetProperty("dispositionType").GetString());
        Assert.Equal("Open", returnItem.GetProperty("status").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            BusinessGatewayTestServiceBaseUrls.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private static BusinessConsoleCreateWmsInboundOrderRequest ValidInboundRequest() =>
        new(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PR-001",
            "S1",
            [new("10", "SKU-001", "EA", 1, "STAGE-01", "LOT-001", null, "qualified", "company", null, new DateOnly(2026, 1, 15), new DateOnly(2027, 1, 15))]);

    private static BusinessConsoleCreateWmsPutawayTaskRequest ValidPutawayRequest() =>
        new("inbound-order-001", "org-001", "env-dev", "PUT-001", "10", "STAGE-01", "BIN-01", 1);

    private static BusinessConsoleCompleteWmsInboundOrderRequest ValidCompleteInboundRequest() =>
        new(
            "inbound-order-001",
            "org-001",
            "env-dev",
            "complete-in-001",
            [new("10", "LOT-CAPTURED-001", new DateOnly(2026, 1, 16), new DateOnly(2027, 1, 16))]);

    private static BusinessConsoleCreateWmsOutboundOrderRequest ValidOutboundRequest() =>
        new(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-shipment",
            "SO-001",
            "S1",
            [new("10", "SKU-001", "EA", 1, "BIN-01", "LOT-001", null, "qualified", "company", null)]);

    private static BusinessConsoleCreateWmsPickingTaskRequest ValidPickingRequest() =>
        new("outbound-order-001", "org-001", "env-dev", "PICK-001", "10", "BIN-01", "SHIP-01", 1);

    private static BusinessConsoleCompleteWmsOutboundOrderRequest ValidCompleteOutboundRequest() =>
        new("outbound-order-001", "org-001", "env-dev", "PACK-001", true, "complete-out-001");

    private static BusinessConsoleRetryWmsOutboundInventoryPostingRequest ValidRetryOutboundRequest() =>
        new("outbound-order-001", "org-001", "env-dev", "retry-out-001");

    private static BusinessConsoleCreateWmsCountExecutionRequest ValidCreateCountRequest() =>
        new("org-001", "env-dev", "COUNT-001", "SKU-001", "EA", "S1", "BIN-01", 1);

    private static BusinessConsoleCompleteWmsCountExecutionRequest ValidCompleteCountRequest() =>
        new("count-execution-001", "org-001", "env-dev", 1, "complete-count-001");

    private static BusinessConsoleDispatchWmsWcsTaskRequest ValidDispatchWcsRequest() =>
        new("warehouse-task-001", "org-001", "env-dev", "agv", "EXT-001", "{}");

    private static BusinessConsoleFailWmsWcsTaskRequest ValidFailWcsRequest() =>
        new("EXT-001", "org-001", "env-dev", "PLC_TIMEOUT", "PLC did not acknowledge.");

    private static BusinessConsoleCompleteWmsWcsTaskRequest ValidCompleteWcsRequest() =>
        new("EXT-001", "org-001", "env-dev", "{}");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) =>
        new(statusCode)
        {
            Content = JsonContent.Create(body),
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string?> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }
}

internal sealed class RecordingWmsClient : IBusinessWmsClient
{
    public List<string> Calls { get; } = [];

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleWmsListRequest? LastInboundRequest { get; private set; }

    public BusinessConsoleWmsListRequest? LastOutboundRequest { get; private set; }

    public BusinessConsoleWmsWarehouseTaskListRequest? LastPutawayTaskRequest { get; private set; }

    public BusinessConsoleWmsWarehouseTaskListRequest? LastPickingTaskRequest { get; private set; }

    public BusinessConsoleWmsCountExecutionListRequest? LastCountExecutionListRequest { get; private set; }

    public BusinessConsoleWmsWcsTaskListRequest? LastWcsTaskRequest { get; private set; }

    public BusinessConsoleWmsReceivingQualityGateListRequest? LastReceivingQualityGateRequest { get; private set; }

    public BusinessConsoleWmsListRequest? LastSupplierReturnRequest { get; private set; }

    public BusinessConsoleCreateWmsInboundOrderRequest? LastCreateInboundRequest { get; private set; }

    public BusinessConsoleCreateWmsPutawayTaskRequest? LastCreatePutawayRequest { get; private set; }

    public BusinessConsoleCompleteWmsInboundOrderRequest? LastCompleteInboundRequest { get; private set; }

    public BusinessConsoleCreateWmsOutboundOrderRequest? LastCreateOutboundRequest { get; private set; }

    public BusinessConsoleCreateWmsPickingTaskRequest? LastCreatePickingRequest { get; private set; }

    public BusinessConsoleCompleteWmsOutboundOrderRequest? LastCompleteOutboundRequest { get; private set; }

    public BusinessConsoleRetryWmsOutboundInventoryPostingRequest? LastRetryOutboundRequest { get; private set; }

    public BusinessConsoleCreateWmsCountExecutionRequest? LastCreateCountRequest { get; private set; }

    public BusinessConsoleCompleteWmsCountExecutionRequest? LastCompleteCountRequest { get; private set; }

    public BusinessConsoleDispatchWmsWcsTaskRequest? LastDispatchWcsRequest { get; private set; }

    public BusinessConsoleFailWmsWcsTaskRequest? LastFailWcsRequest { get; private set; }

    public BusinessConsoleCompleteWmsWcsTaskRequest? LastCompleteWcsRequest { get; private set; }

    public Task<BusinessConsoleCreateWmsInboundOrderResponse> CreateInboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsInboundOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreateInboundRequest = request;
        Calls.Add("create-inbound");
        return Task.FromResult(new BusinessConsoleCreateWmsInboundOrderResponse("inbound-order-001"));
    }

    public Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePutawayTaskAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCreateWmsPutawayTaskRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreatePutawayRequest = request;
        Calls.Add("create-putaway");
        return Task.FromResult(new BusinessConsoleCreateWmsWarehouseTaskResponse("warehouse-task-001"));
    }

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessConsoleCompleteWmsInboundOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteInboundRequest = request;
        Calls.Add("complete-inbound");
        return Task.FromResult(new BusinessConsoleCompleteWmsMovementResponse("request-in-001", "movement-in-001"));
    }

    public Task<BusinessConsoleCreateWmsOutboundOrderResponse> CreateOutboundOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsOutboundOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreateOutboundRequest = request;
        Calls.Add("create-outbound");
        return Task.FromResult(new BusinessConsoleCreateWmsOutboundOrderResponse("outbound-order-001"));
    }

    public Task<BusinessConsoleCreateWmsWarehouseTaskResponse> CreatePickingTaskAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCreateWmsPickingTaskRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreatePickingRequest = request;
        Calls.Add("create-picking");
        return Task.FromResult(new BusinessConsoleCreateWmsWarehouseTaskResponse("warehouse-task-002"));
    }

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleCompleteWmsOutboundOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteOutboundRequest = request;
        Calls.Add("complete-outbound");
        return Task.FromResult(new BusinessConsoleCompleteWmsMovementResponse("request-out-001", "movement-out-001"));
    }

    public Task<BusinessConsoleCompleteWmsMovementResponse> RetryOutboundInventoryPostingAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessConsoleRetryWmsOutboundInventoryPostingRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastRetryOutboundRequest = request;
        Calls.Add("retry-outbound");
        return Task.FromResult(new BusinessConsoleCompleteWmsMovementResponse("request-out-retry-001", null));
    }

    public Task<BusinessConsoleCreateWmsCountExecutionResponse> CreateCountExecutionAsync(
        string internalBearerToken,
        BusinessConsoleCreateWmsCountExecutionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCreateCountRequest = request;
        Calls.Add("create-count");
        return Task.FromResult(new BusinessConsoleCreateWmsCountExecutionResponse("count-execution-001"));
    }

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessConsoleCompleteWmsCountExecutionRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteCountRequest = request;
        Calls.Add("complete-count");
        return Task.FromResult(new BusinessConsoleCompleteWmsMovementResponse("request-count-001", "movement-count-001"));
    }

    public Task<BusinessConsoleDispatchWmsWcsTaskResponse> DispatchWcsTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessConsoleDispatchWmsWcsTaskRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastDispatchWcsRequest = request;
        Calls.Add("dispatch-wcs");
        return Task.FromResult(new BusinessConsoleDispatchWmsWcsTaskResponse("wcs-task-001"));
    }

    public Task<BusinessConsoleAcceptedResponse> FailWcsTaskAsync(
        string internalBearerToken,
        string externalTaskId,
        BusinessConsoleFailWmsWcsTaskRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastFailWcsRequest = request;
        Calls.Add("fail-wcs");
        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
    }

    public Task<BusinessConsoleAcceptedResponse> CompleteWcsTaskAsync(
        string internalBearerToken,
        string externalTaskId,
        BusinessConsoleCompleteWmsWcsTaskRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteWcsRequest = request;
        Calls.Add("complete-wcs");
        return Task.FromResult(new BusinessConsoleAcceptedResponse(true));
    }

    public Task<BusinessConsoleWmsInboundOrderListResponse> ListInboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastInboundRequest = request;
        return Task.FromResult(new BusinessConsoleWmsInboundOrderListResponse(
        [
            new BusinessConsoleWmsInboundOrderItem(
                "inbound-order-001",
                "IN-001",
                "Created",
                DateTime.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                "pending",
                false),
        ],
        47,
        null,
        "unsupported"));
    }

    public Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastOutboundRequest = request;
        return Task.FromResult(new BusinessConsoleWmsOutboundOrderListResponse(
        [
            new BusinessConsoleWmsOutboundOrderItem(
                "outbound-order-001",
                "OUT-001",
                "Created",
                "finished-goods",
                "failed",
                "NEGATIVE_ON_HAND",
                "Stock movement would make on-hand quantity negative.",
                [
                    new BusinessConsoleWmsOutboundOrderLineItem(
                        "SO-LINE-001",
                        "SKU-FG-1000",
                        "kg",
                        4m,
                        4m,
                        "receiving",
                        "LOT-001",
                        null,
                        "unrestricted",
                        "production",
                        null,
                        "failed",
                        "NEGATIVE_ON_HAND",
                        "Stock movement would make on-hand quantity negative."),
                ],
                DateTime.Parse("2026-06-01T09:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                null),
        ],
        31));
    }

    public Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPutawayTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWarehouseTaskListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastPutawayTaskRequest = request;
        return Task.FromResult(new BusinessConsoleWmsWarehouseTaskListResponse(
        [
            new BusinessConsoleWmsWarehouseTaskItem(
                "warehouse-task-putaway-001",
                "org-001",
                "env-dev",
                "Putaway",
                "PUT-001",
                "IN-001",
                "10",
                "SKU-001",
                "EA",
                "S1",
                "RECV-01",
                "BIN-01",
                3,
                0,
                "Open",
                DateTime.Parse("2026-06-01T09:30:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                null),
        ],
        29));
    }

    public Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPickingTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWarehouseTaskListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastPickingTaskRequest = request;
        return Task.FromResult(new BusinessConsoleWmsWarehouseTaskListResponse(
        [
            new BusinessConsoleWmsWarehouseTaskItem(
                "warehouse-task-picking-001",
                "org-001",
                "env-dev",
                "Picking",
                "PICK-001",
                "OUT-001",
                "10",
                "SKU-001",
                "EA",
                "S1",
                "BIN-01",
                "SHIP-01",
                2,
                0,
                "Open",
                DateTime.Parse("2026-06-01T09:40:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                null),
        ],
        23));
    }

    public Task<BusinessConsoleWmsCountExecutionListResponse> ListCountExecutionsAsync(
        string internalBearerToken,
        BusinessConsoleWmsCountExecutionListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCountExecutionListRequest = request;
        return Task.FromResult(new BusinessConsoleWmsCountExecutionListResponse(
        [
            new BusinessConsoleWmsCountExecutionItem(
                "count-execution-001",
                "org-001",
                "env-dev",
                "COUNT-001",
                "SKU-001",
                "EA",
                "S1",
                "BIN-02",
                9,
                null,
                null,
                "Open",
                DateTime.Parse("2026-06-01T09:50:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                null),
        ],
        17));
    }

    public Task<BusinessConsoleWmsWcsTaskListResponse> ListWcsTasksAsync(
        string internalBearerToken,
        BusinessConsoleWmsWcsTaskListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastWcsTaskRequest = request;
        return Task.FromResult(new BusinessConsoleWmsWcsTaskListResponse(
        [
            new BusinessConsoleWmsWcsTaskItem(
                "wcs-task-001",
                "org-001",
                "env-dev",
                "warehouse-task-001",
                "demo",
                "EXT-001",
                "Dispatched",
                1,
                null,
                null,
                DateTime.Parse("2026-06-01T10:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                null,
                null),
        ],
        14));
    }

    public Task<BusinessConsoleWmsReceivingQualityGateListResponse> ListReceivingQualityGatesAsync(
        string internalBearerToken,
        BusinessConsoleWmsReceivingQualityGateListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastReceivingQualityGateRequest = request;
        return Task.FromResult(new BusinessConsoleWmsReceivingQualityGateListResponse(
        [
            new BusinessConsoleWmsReceivingQualityGateItem(
                "inbound-order-001",
                "inbound-order-line-001",
                "org-001",
                "env-dev",
                "IN-GATE-001",
                "Completed",
                "S1",
                "10",
                "SKU-FG-1000",
                "kg",
                5,
                "STAGE-01",
                "LOT-001",
                null,
                new DateOnly(2026, 1, 15),
                new DateOnly(2027, 1, 15),
                "quality",
                "rejected",
                "QI-REJ-001",
                "critical-defect",
                "company",
                null,
                DateTime.Parse("2026-06-01T10:10:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)),
        ],
        41));
    }

    public Task<BusinessConsoleWmsSupplierReturnListResponse> ListSupplierReturnRequestsAsync(
        string internalBearerToken,
        BusinessConsoleWmsListRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastSupplierReturnRequest = request;
        return Task.FromResult(new BusinessConsoleWmsSupplierReturnListResponse(
        [
            new BusinessConsoleWmsSupplierReturnItem(
                "supplier-return-001",
                "org-001",
                "env-dev",
                "RTS-IN-GATE-001-10-QI-REJ-001",
                "IN-GATE-001",
                "10",
                "QI-REJ-001",
                "SKU-FG-1000",
                "kg",
                "S1",
                "STAGE-01",
                "LOT-001",
                null,
                "company",
                null,
                5,
                "return-to-supplier",
                "critical-defect",
                "Open",
                DateTime.Parse("2026-06-01T10:20:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)),
        ],
        37));
    }
}
