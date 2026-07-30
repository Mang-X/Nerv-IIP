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
using Nerv.IIP.Contracts.Iam;
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
                "/api/business/v1/wms/putaway-tasks/putaway-task-001/start" or
                "/api/business/v1/wms/putaway-tasks/putaway-task-001/progress" or
                "/api/business/v1/wms/putaway-tasks/putaway-task-001/exception" or
                "/api/business/v1/wms/putaway-tasks/putaway-task-001/complete" or
                "/api/business/v1/wms/picking-tasks/picking-task-001/start" or
                "/api/business/v1/wms/picking-tasks/picking-task-001/progress" or
                "/api/business/v1/wms/picking-tasks/picking-task-001/exception" or
                "/api/business/v1/wms/picking-tasks/picking-task-001/complete" => new
                {
                    warehouseTaskId = path.Contains("putaway", StringComparison.Ordinal)
                        ? "putaway-task-001"
                        : "picking-task-001",
                    taskType = path.Contains("putaway", StringComparison.Ordinal) ? "Putaway" : "Picking",
                    status = "InProgress",
                    version = 4,
                    executedQuantity = 1,
                    differenceQuantity = 0,
                    allowedActions = Array.Empty<string>(),
                    blockReasons = Array.Empty<string>(),
                },
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
        await client.StartPutawayTaskAsync("internal-token-001", "putaway-task-001", ValidStartTaskAction("putaway-task-001", poolCode: "POOL-A"), CancellationToken.None);
        await client.RecordPutawayTaskProgressAsync("internal-token-001", "putaway-task-001", ValidProgressTaskAction("putaway-task-001", poolCode: "POOL-A"), CancellationToken.None);
        await client.ReportPutawayTaskExceptionAsync("internal-token-001", "putaway-task-001", ValidExceptionTaskAction("putaway-task-001", poolCode: "POOL-A"), CancellationToken.None);
        await client.CompletePutawayTaskAsync("internal-token-001", "putaway-task-001", ValidCompleteTaskAction("putaway-task-001", poolCode: "POOL-A"), CancellationToken.None);
        await client.StartPickingTaskAsync("internal-token-001", "picking-task-001", ValidStartTaskAction("picking-task-001", siteCode: "SITE-A"), CancellationToken.None);
        await client.RecordPickingTaskProgressAsync("internal-token-001", "picking-task-001", ValidProgressTaskAction("picking-task-001", siteCode: "SITE-A"), CancellationToken.None);
        await client.ReportPickingTaskExceptionAsync("internal-token-001", "picking-task-001", ValidExceptionTaskAction("picking-task-001", siteCode: "SITE-A"), CancellationToken.None);
        await client.CompletePickingTaskAsync("internal-token-001", "picking-task-001", ValidCompleteTaskAction("picking-task-001", siteCode: "SITE-A"), CancellationToken.None);

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
            "POST /api/business/v1/wms/putaway-tasks/putaway-task-001/start",
            "POST /api/business/v1/wms/putaway-tasks/putaway-task-001/progress",
            "POST /api/business/v1/wms/putaway-tasks/putaway-task-001/exception",
            "POST /api/business/v1/wms/putaway-tasks/putaway-task-001/complete",
            "POST /api/business/v1/wms/picking-tasks/picking-task-001/start",
            "POST /api/business/v1/wms/picking-tasks/picking-task-001/progress",
            "POST /api/business/v1/wms/picking-tasks/picking-task-001/exception",
            "POST /api/business/v1/wms/picking-tasks/picking-task-001/complete",
        ],
        handler.Requests.Select(request => $"{request.Method} {request.RequestUri!.AbsolutePath}").ToArray());
        Assert.All(handler.Requests, request => Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter));
        Assert.Equal("complete-in-001", handler.Requests[2].Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("complete-out-001", handler.Requests[5].Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("complete-count-001", handler.Requests[8].Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("request-in-http", completedInbound.RequestId);
        Assert.Equal("complete-in-001", completedInbound.OperationReceipt?.IdempotencyKey);
        Assert.True(completedInbound.OperationReceipt?.ReadbackRequired);
        Assert.False(completedInbound.OperationReceipt?.StateConfirmed);

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
        Assert.Contains("\"actorPrincipalId\":\"user-admin\"", handler.RequestBodies[12], StringComparison.Ordinal);
        Assert.Contains("\"scopeKind\":\"work-pool\"", handler.RequestBodies[12], StringComparison.Ordinal);
        Assert.Contains("\"scopeId\":\"POOL-A\"", handler.RequestBodies[12], StringComparison.Ordinal);
        Assert.Contains("\"actorPrincipalId\":\"user-admin\"", handler.RequestBodies[16], StringComparison.Ordinal);
        Assert.Contains("\"authorizedSiteCodes\":[\"SITE-A\"]", handler.RequestBodies[16], StringComparison.Ordinal);
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

        var inbound = await client.ListInboundOrdersAsync(
            "internal-token-001",
            new BusinessWmsScopedListRequest(
                "org-001",
                "env-dev",
                "user-001",
                ["SITE-A"],
                "self",
                "user-001",
                LocationCode: null,
                LotNo: null,
                SiteCode: "SITE-A",
                Skip: 10,
                Take: 20,
                Status: "Open",
                Keyword: "IN-001"),
            "0199aa00-0000-7000-8000-000000000001",
            CancellationToken.None);
        var putaway = await client.ListPutawayTasksAsync(
            "internal-token-001",
            new BusinessWmsWarehouseTaskListRequest(
                "org-001",
                "env-dev",
                "user-001",
                ["SITE-A"],
                "work-pool",
                "POOL-A",
                "RECV-01",
                "LOT-001",
                "SITE-A",
                15,
                25,
                "Open",
                "PUT-001"),
            CancellationToken.None);
        var outbound = await client.ListOutboundOrdersAsync(
            "internal-token-001",
            new BusinessWmsScopedListRequest(
                "org-001",
                "env-dev",
                "user-001",
                ["SITE-A"],
                "site",
                "SITE-A",
                Skip: 20,
                Take: 10,
                Status: "Completed",
                Keyword: "OUT-001"),
            "0199aa00-0000-7000-8000-000000000002",
            CancellationToken.None);
        var picking = await client.ListPickingTasksAsync(
            "internal-token-001",
            new BusinessWmsWarehouseTaskListRequest(
                "org-001",
                "env-dev",
                "user-001",
                ["SITE-A"],
                "work-pool",
                "POOL-B",
                "BIN-01",
                null,
                "SITE-A",
                25,
                35,
                "Open",
                "PICK-001"),
            CancellationToken.None);
        var count = await client.ListCountExecutionsAsync(
            "internal-token-001",
            new BusinessWmsCountExecutionListRequest(
                "org-001",
                "env-dev",
                "user-001",
                ["SITE-A"],
                "site",
                "SITE-A",
                "BIN-02",
                "SITE-A",
                5,
                15,
                "Open",
                "COUNT-001",
                "0199aa00-0000-7000-8000-000000000003"),
            CancellationToken.None);
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
            "GET /api/business/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&actorPrincipalId=user-001&scopeKind=self&scopeId=user-001&siteCode=SITE-A&skip=10&take=20&status=Open&keyword=IN-001&inboundOrderId=0199aa00-0000-7000-8000-000000000001&authorizedSiteCodes=SITE-A",
            "GET /api/business/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&actorPrincipalId=user-001&scopeKind=work-pool&scopeId=POOL-A&locationCode=RECV-01&lotNo=LOT-001&siteCode=SITE-A&skip=15&take=25&status=Open&keyword=PUT-001&authorizedSiteCodes=SITE-A",
            "GET /api/business/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&actorPrincipalId=user-001&scopeKind=site&scopeId=SITE-A&skip=20&take=10&status=Completed&keyword=OUT-001&outboundOrderId=0199aa00-0000-7000-8000-000000000002&authorizedSiteCodes=SITE-A",
            "GET /api/business/v1/wms/picking-tasks?organizationId=org-001&environmentId=env-dev&actorPrincipalId=user-001&scopeKind=work-pool&scopeId=POOL-B&locationCode=BIN-01&siteCode=SITE-A&skip=25&take=35&status=Open&keyword=PICK-001&authorizedSiteCodes=SITE-A",
            "GET /api/business/v1/wms/count-executions?organizationId=org-001&environmentId=env-dev&actorPrincipalId=user-001&scopeKind=site&scopeId=SITE-A&locationCode=BIN-02&siteCode=SITE-A&skip=5&take=15&status=Open&keyword=COUNT-001&countExecutionId=0199aa00-0000-7000-8000-000000000003&authorizedSiteCodes=SITE-A",
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
        var auth = OrganizationScopeAuth(BusinessGatewayPermissions.WmsReceiptsManage);
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
            expectedVersion = 3,
            actorPrincipalId = "forged-user",
            authorizedSiteCodes = new[] { "FORGED-SITE" },
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
            expectedVersion = 3,
            actorPrincipalId = "forged-user",
            authorizedSiteCodes = new[] { "FORGED-SITE" },
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
        Assert.Equal("user-admin", wms.LastCompleteInboundRequest.ActorPrincipalId);
        Assert.Equal(["S1"], wms.LastCompleteInboundRequest.AuthorizedSiteCodes);
        Assert.Equal("self", wms.LastCompleteInboundRequest.ScopeKind);
        Assert.Equal("user-admin", wms.LastCompleteInboundRequest.ScopeId);
        Assert.Equal(3, wms.LastCompleteInboundRequest.ExpectedVersion);
        var completeInboundLine = Assert.Single(wms.LastCompleteInboundRequest.Lines!);
        Assert.Equal("10", completeInboundLine.LineNo);
        Assert.Equal("LOT-CAPTURED-001", completeInboundLine.LotNo);
        Assert.Equal(new DateOnly(2026, 1, 16), completeInboundLine.ProductionDate);
        Assert.Equal(new DateOnly(2027, 1, 16), completeInboundLine.ExpiryDate);
        Assert.Equal("COUNT-001", wms.LastCreateCountRequest!.CountNo);
        Assert.Equal("count-execution-001", wms.LastCompleteCountRequest!.CountExecutionId);
        Assert.Equal("user-admin", wms.LastCompleteCountRequest.ActorPrincipalId);
        Assert.Equal(["S1"], wms.LastCompleteCountRequest.AuthorizedSiteCodes);
        Assert.Equal("self", wms.LastCompleteCountRequest.ScopeKind);
        Assert.Equal("user-admin", wms.LastCompleteCountRequest.ScopeId);
        Assert.Equal(3, wms.LastCompleteCountRequest.ExpectedVersion);
    }

    [Fact]
    public async Task Shipment_write_facades_use_shipments_manage_permission_internal_token_and_route_ids()
    {
        var wms = new RecordingWmsClient();
        var auth = OrganizationScopeAuth(BusinessGatewayPermissions.WmsShipmentsManage);
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
            expectedVersion = 3,
            actorPrincipalId = "forged-user",
            authorizedSiteCodes = new[] { "FORGED-SITE" },
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
        Assert.Equal("user-admin", wms.LastCompleteOutboundRequest.ActorPrincipalId);
        Assert.Equal(["S1"], wms.LastCompleteOutboundRequest.AuthorizedSiteCodes);
        Assert.Equal("self", wms.LastCompleteOutboundRequest.ScopeKind);
        Assert.Equal("user-admin", wms.LastCompleteOutboundRequest.ScopeId);
        Assert.Equal(3, wms.LastCompleteOutboundRequest.ExpectedVersion);
        Assert.Equal("outbound-order-001", wms.LastRetryOutboundRequest!.OutboundOrderId);
        Assert.Equal("retry-out-001", wms.LastRetryOutboundRequest.IdempotencyKey);
    }

    [Fact]
    public async Task Wcs_write_facades_use_automation_permission_internal_token_and_route_ids()
    {
        var wms = new RecordingWmsClient();
        var auth = OrganizationScopeAuth(BusinessGatewayPermissions.WmsAutomationManage);
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
            expectedVersion = 3,
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
        Assert.Equal("user-admin", wms.LastDispatchWcsRequest.DispatcherPrincipalId);
        Assert.Equal(["S1"], wms.LastDispatchWcsRequest.AuthorizedSiteCodes);
        Assert.Equal(3, wms.LastDispatchWcsRequest.ExpectedVersion);
        Assert.Equal("EXT-001", wms.LastFailWcsRequest!.ExternalTaskId);
        Assert.Equal("EXT-001", wms.LastCompleteWcsRequest!.ExternalTaskId);
    }

    [Fact]
    public async Task Wms_work_scope_catalogs_inject_principal_and_exact_sites_for_each_capability()
    {
        var wms = new RecordingWmsClient();
        var auth = ScopeAuth(
            [
                BusinessGatewayPermissions.WmsReceiptsRead,
                BusinessGatewayPermissions.WmsShipmentsRead,
            ],
            new AuthorizationScopeGrant(
                "membership",
                "warehouse-membership",
                "site",
                "SITE-B",
                [
                    BusinessGatewayPermissions.WmsReceiptsRead,
                    BusinessGatewayPermissions.WmsShipmentsRead,
                ]),
            new AuthorizationScopeGrant(
                "role",
                "role-warehouse",
                "site",
                "SITE-A",
                [
                    BusinessGatewayPermissions.WmsReceiptsRead,
                    BusinessGatewayPermissions.WmsShipmentsRead,
                ]),
            new AuthorizationScopeGrant(
                "user",
                "untrusted-direct-grant",
                "site",
                "FORGED-SITE",
                [
                    BusinessGatewayPermissions.WmsReceiptsRead,
                    BusinessGatewayPermissions.WmsShipmentsRead,
                ]));
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        const string forged =
            "?organizationId=org-001&environmentId=env-dev&actorPrincipalId=forged&authorizedSiteCodes=FORGED-SITE";

        foreach (var path in new[]
                 {
                     "/api/business-console/v1/wms/work-scopes/receipts",
                     "/api/business-console/v1/wms/work-scopes/shipments",
                     "/api/business-console/v1/wms/work-scopes/counts",
                 })
        {
            var response = await client.GetAsync(path + forged);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("user-admin", wms.LastWorkScopeCatalogRequest!.ActorPrincipalId);
            Assert.Equal(
                ["SITE-A", "SITE-B"],
                wms.LastWorkScopeCatalogRequest.AuthorizedSiteCodes);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("user-admin", data.GetProperty("actorPrincipalId").GetString());
            Assert.Equal(
                ["self", "work-pool", "site"],
                data.GetProperty("items")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("scopeKind").GetString()!)
                    .ToArray());
        }

        Assert.Equal(
            ["receipt-scopes", "shipment-scopes", "count-scopes"],
            wms.Calls);
        Assert.Equal(
            [
                BusinessGatewayPermissions.WmsReceiptsRead,
                BusinessGatewayPermissions.WmsShipmentsRead,
                BusinessGatewayPermissions.WmsReceiptsRead,
            ],
            auth.Requirements.Select(requirement => requirement.PermissionCode).ToArray());
        Assert.All(auth.Requirements, requirement => Assert.True(requirement.IncludePrincipalContext));
        Assert.Equal(
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired,
            auth.LastContinuityMode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
    }

    [Fact]
    public async Task Wms_assignment_facades_inject_trusted_assigner_sites_and_route_resource_ids()
    {
        var wms = new RecordingWmsClient();
        var auth = ScopeAuth(
            [
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
            ],
            new AuthorizationScopeGrant(
                "role",
                "role-warehouse",
                "site",
                "SITE-A",
                [
                    BusinessGatewayPermissions.WmsReceiptsManage,
                    BusinessGatewayPermissions.WmsShipmentsManage,
                ]));
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        var scenarios = new[]
        {
            (
                "/api/business-console/v1/wms/inbound-orders/inbound-001/assignment",
                "inbound-001",
                "assign-inbound"),
            (
                "/api/business-console/v1/wms/putaway-tasks/putaway-001/assignment",
                "putaway-001",
                "assign-putaway"),
            (
                "/api/business-console/v1/wms/outbound-orders/outbound-001/assignment",
                "outbound-001",
                "assign-outbound"),
            (
                "/api/business-console/v1/wms/picking-tasks/picking-001/assignment",
                "picking-001",
                "assign-picking"),
            (
                "/api/business-console/v1/wms/count-executions/count-001/assignment",
                "count-001",
                "assign-count"),
        };

        foreach (var (path, resourceId, idempotencyKey) in scenarios)
        {
            var response = await client.PostAsJsonAsync(
                path + "?organizationId=org-001&environmentId=env-dev",
                new
                {
                    inboundOrderId = "forged-resource",
                    outboundOrderId = "forged-resource",
                    warehouseTaskId = "forged-resource",
                    countExecutionId = "forged-resource",
                    poolCode = "POOL-WAREHOUSE",
                    operatorPrincipalId = "user-emp-049",
                    idempotencyKey,
                    expectedVersion = 3,
                    assignerPrincipalId = "forged-principal",
                    authorizedSiteCodes = new[] { "FORGED-SITE" },
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertTrustedAssignment(
                wms.LastAssignmentRequest!,
                resourceId,
                idempotencyKey);
        }

        Assert.Equal(
            [
                "assign-inbound-order",
                "assign-putaway-task",
                "assign-outbound-order",
                "assign-picking-task",
                "assign-count-execution",
            ],
            wms.Calls);
        Assert.Equal(
            [
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
                BusinessGatewayPermissions.WmsReceiptsManage,
            ],
            auth.Requirements.Select(requirement => requirement.PermissionCode).ToArray());
        Assert.All(auth.Requirements, requirement => Assert.True(requirement.IncludePrincipalContext));
        Assert.Equal(
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired,
            auth.LastContinuityMode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "stale-version")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "assignment-target-invalid")]
    public async Task Wms_assignment_facade_preserves_governed_downstream_failure(
        HttpStatusCode statusCode,
        string safeCode)
    {
        var wms = new RecordingWmsClient
        {
            AssignmentFailure =
                BusinessServiceProxyException.FromSafeDownstreamMessage(
                    statusCode,
                    safeCode),
        };
        var auth = ScopeAuth(
            [BusinessGatewayPermissions.WmsReceiptsManage],
            new AuthorizationScopeGrant(
                "role",
                "role-warehouse",
                "site",
                "SITE-A",
                [BusinessGatewayPermissions.WmsReceiptsManage]));
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/wms/inbound-orders/inbound-001/assignment?organizationId=org-001&environmentId=env-dev",
            new
            {
                poolCode = "POOL-WAREHOUSE",
                operatorPrincipalId = "user-emp-049",
                idempotencyKey = "assign-inbound",
                expectedVersion = 3,
            });

        Assert.Equal(statusCode, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(safeCode, document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Wms_catalog_and_assignment_fail_closed_without_exact_site_grant()
    {
        var wms = new RecordingWmsClient();
        var auth = ScopeAuth(
            [
                BusinessGatewayPermissions.WmsReceiptsRead,
                BusinessGatewayPermissions.WmsReceiptsManage,
            ],
            new AuthorizationScopeGrant(
                "role",
                "role-warehouse",
                "self",
                "user-admin",
                [
                    BusinessGatewayPermissions.WmsReceiptsRead,
                    BusinessGatewayPermissions.WmsReceiptsManage,
                ]));
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var catalog = await client.GetAsync(
            "/api/business-console/v1/wms/work-scopes/receipts?organizationId=org-001&environmentId=env-dev");
        var assignment = await client.PostAsJsonAsync(
            "/api/business-console/v1/wms/inbound-orders/inbound-001/assignment?organizationId=org-001&environmentId=env-dev",
            new
            {
                poolCode = "POOL-WAREHOUSE",
                operatorPrincipalId = "user-emp-049",
                idempotencyKey = "assign-inbound",
                expectedVersion = 3,
            });

        Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assignment.StatusCode);
        Assert.Empty(wms.Calls);
        Assert.Null(wms.LastWorkScopeCatalogRequest);
        Assert.Null(wms.LastAssignmentRequest);
    }

    [Fact]
    public async Task Inbound_orders_include_inventory_context_in_single_facade_response()
    {
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(OrganizationScopeAuth(
            BusinessGatewayPermissions.WmsReceiptsRead,
            BusinessGatewayPermissions.InventoryLedgerRead), services =>
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

        var response = await client.GetAsync("/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1&skip=10&take=20&status=Open&keyword=IN&inboundOrderId=0199aa00-0000-7000-8000-000000000001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equal("internal-test-token", inventory.LastInternalToken);
        Assert.Equivalent(
            new BusinessWmsScopedListRequest(
                "org-001",
                "env-dev",
                "user-admin",
                ["S1"],
                "self",
                "user-admin",
                SiteCode: "S1",
                Skip: 10,
                Take: 20,
                Status: "Open",
                Keyword: "IN"),
            wms.LastInboundRequest,
            strict: true);
        Assert.Equal("0199aa00-0000-7000-8000-000000000001", wms.LastInboundOrderId);
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
        await using var factory = CreateFactory(
            OrganizationScopeAuth(BusinessGatewayPermissions.WmsReceiptsRead),
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
            OrganizationScopeAuth(BusinessGatewayPermissions.WmsReceiptsRead),
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
    [InlineData("site")]
    [InlineData("self")]
    [InlineData("team")]
    [InlineData("no-grant")]
    public async Task Inbound_inventory_context_does_not_query_site_outside_independent_inventory_scope(
        string inventoryScope)
    {
        var inventoryGrant = inventoryScope switch
        {
            "site" => new AuthorizationScopeGrant(
                "role",
                "role-inventory-site",
                "site",
                "SITE-A",
                [BusinessGatewayPermissions.InventoryLedgerRead]),
            "self" => new AuthorizationScopeGrant(
                "role",
                "role-inventory-self",
                "self",
                "user-admin",
                [BusinessGatewayPermissions.InventoryLedgerRead]),
            "team" => new AuthorizationScopeGrant(
                "role",
                "role-inventory-team",
                "team",
                "TEAM-A",
                [BusinessGatewayPermissions.InventoryLedgerRead]),
            _ => null,
        };
        var grants = new List<AuthorizationScopeGrant>
        {
            new(
                "role",
                "role-wms-site",
                "site",
                "S1",
                [BusinessGatewayPermissions.WmsReceiptsRead]),
        };
        if (inventoryGrant is not null)
        {
            grants.Add(inventoryGrant);
        }

        var auth = ScopeAuth(
            [
                BusinessGatewayPermissions.WmsReceiptsRead,
                BusinessGatewayPermissions.InventoryLedgerRead,
            ],
            [.. grants]);
        var wms = new RecordingWmsClient();
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, inventory.AvailabilityCallCount);
        Assert.Equal(
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired,
            auth.LastContinuityMode);
        Assert.True(auth.Requirements.Last().IncludePrincipalContext);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var context = document.RootElement.GetProperty("data").GetProperty("inventoryContext");
        Assert.Equal("forbidden", context.GetProperty("status").GetString());
        Assert.Equal("work-scope-not-authorized", context.GetProperty("reason").GetString());
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
        await using var factory = CreateFactory(OrganizationScopeAuth(
            BusinessGatewayPermissions.WmsReceiptsRead,
            BusinessGatewayPermissions.InventoryLedgerRead), services =>
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
        await using var factory = CreateFactory(
            OrganizationScopeAuth(BusinessGatewayPermissions.WmsShipmentsRead),
            services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&skip=20&take=10&status=Completed&keyword=OUT&outboundOrderId=0199aa00-0000-7000-8000-000000000002");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
        Assert.Equivalent(
            new BusinessWmsScopedListRequest(
                "org-001",
                "env-dev",
                "user-admin",
                ["S1"],
                "self",
                "user-admin",
                Skip: 20,
                Take: 10,
                Status: "Completed",
                Keyword: "OUT"),
            wms.LastOutboundRequest,
            strict: true);
        Assert.Equal("0199aa00-0000-7000-8000-000000000002", wms.LastOutboundOrderId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(31, data.GetProperty("total").GetInt32());
        Assert.Equal("OUT-001", data.GetProperty("items")[0].GetProperty("outboundOrderNo").GetString());
    }

    [Fact]
    public async Task Outbound_order_list_rejects_invalid_scope_paging_filters_and_exact_id()
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
        var invalidRequests = new[]
        {
            "/api/business-console/v1/wms/outbound-orders?environmentId=env-dev",
            "/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&skip=-1",
            "/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&take=0",
            $"/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&status={new string('s', 51)}",
            $"/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&keyword={new string('k', 151)}",
            "/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&outboundOrderId=not-a-guid",
        };

        foreach (var requestUri in invalidRequests)
        {
            var response = await client.GetAsync(requestUri);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Null(wms.LastOutboundRequest);
        Assert.Null(wms.LastOutboundOrderId);
    }

    [Fact]
    public async Task Wms_task_and_count_lists_use_read_permissions_internal_token_and_filters()
    {
        var wms = new RecordingWmsClient();
        var auth = OrganizationScopeAuth(
            BusinessGatewayPermissions.WmsReceiptsRead,
            BusinessGatewayPermissions.WmsShipmentsRead);
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var putaway = await client.GetAsync("/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&locationCode=RECV-01&lotNo=LOT-001&skip=10&take=20&status=Open&keyword=PUT");
        var picking = await client.GetAsync("/api/business-console/v1/wms/picking-tasks?organizationId=org-001&environmentId=env-dev&locationCode=BIN-01&skip=20&take=10&status=Open&keyword=PICK");
        var count = await client.GetAsync("/api/business-console/v1/wms/count-executions?organizationId=org-001&environmentId=env-dev&locationCode=BIN-02&skip=5&take=15&status=Open&keyword=COUNT&countExecutionId=0199aa00-0000-7000-8000-000000000003");

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
        Assert.Equivalent(
            new BusinessWmsWarehouseTaskListRequest(
                "org-001",
                "env-dev",
                "user-admin",
                ["S1"],
                "self",
                "user-admin",
                "RECV-01",
                "LOT-001",
                null,
                Skip: 10,
                Take: 20,
                Status: "Open",
                Keyword: "PUT"),
            wms.LastPutawayTaskRequest,
            strict: true);
        Assert.Equivalent(
            new BusinessWmsWarehouseTaskListRequest(
                "org-001",
                "env-dev",
                "user-admin",
                ["S1"],
                "self",
                "user-admin",
                "BIN-01",
                null,
                null,
                Skip: 20,
                Take: 10,
                Status: "Open",
                Keyword: "PICK"),
            wms.LastPickingTaskRequest,
            strict: true);
        Assert.Equivalent(
            new BusinessWmsCountExecutionListRequest(
                "org-001",
                "env-dev",
                "user-admin",
                ["S1"],
                "self",
                "user-admin",
                "BIN-02",
                null,
                Skip: 5,
                Take: 15,
                Status: "Open",
                Keyword: "COUNT",
                CountExecutionId: "0199aa00-0000-7000-8000-000000000003"),
            wms.LastCountExecutionListRequest,
            strict: true);

        using var putawayDocument = JsonDocument.Parse(await putaway.Content.ReadAsStringAsync());
        using var pickingDocument = JsonDocument.Parse(await picking.Content.ReadAsStringAsync());
        using var countDocument = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
        Assert.Equal("PUT-001", putawayDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("taskNo").GetString());
        Assert.Equal("PICK-001", pickingDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("taskNo").GetString());
        Assert.Equal("COUNT-001", countDocument.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("countNo").GetString());
        var countItem = countDocument.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("Failed", countItem.GetProperty("inventoryPostingStatus").GetString());
        Assert.Equal("NEGATIVE_ON_HAND", countItem.GetProperty("inventoryPostingFailureCode").GetString());
        Assert.Equal(
            "Stock movement would make on-hand quantity negative.",
            countItem.GetProperty("inventoryPostingFailureMessage").GetString());
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

    [Fact]
    public async Task Wms_scoped_lists_derive_ownership_from_authorized_scope_and_ignore_forged_internal_filters()
    {
        var wms = new RecordingWmsClient();
        var auth = ScopeAuth(
            [
                BusinessGatewayPermissions.WmsReceiptsRead,
                BusinessGatewayPermissions.WmsShipmentsRead,
            ],
            new AuthorizationScopeGrant(
                "role",
                "role-warehouse",
                "site",
                "SITE-A",
                [
                    BusinessGatewayPermissions.WmsReceiptsRead,
                    BusinessGatewayPermissions.WmsShipmentsRead,
                ]));
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        const string forged = "&actorPrincipalId=forged&authorizedSiteCodes=forged&assignedOperatorUserIds=forged&assignedPoolCodes=forged";

        var inbound = await client.GetAsync(
            "/api/business-console/v1/wms/inbound-orders?organizationId=org-001&environmentId=env-dev&scopeKind=self&scopeId=user-admin" + forged);

        Assert.Equal(HttpStatusCode.OK, inbound.StatusCode);
        Assert.Equal("user-admin", wms.LastInboundRequest!.ActorPrincipalId);
        Assert.Equal(["SITE-A"], wms.LastInboundRequest.AuthorizedSiteCodes);
        Assert.Equal("self", wms.LastInboundRequest.ScopeKind);
        Assert.Equal("user-admin", wms.LastInboundRequest.ScopeId);

        var putaway = await client.GetAsync(
            "/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&scopeKind=work-pool&scopeId=POOL-A" + forged);

        Assert.Equal(HttpStatusCode.OK, putaway.StatusCode);
        Assert.Equal("user-admin", wms.LastPutawayTaskRequest!.ActorPrincipalId);
        Assert.Equal(["SITE-A"], wms.LastPutawayTaskRequest.AuthorizedSiteCodes);
        Assert.Equal("work-pool", wms.LastPutawayTaskRequest.ScopeKind);
        Assert.Equal("POOL-A", wms.LastPutawayTaskRequest.ScopeId);

        var count = await client.GetAsync(
            "/api/business-console/v1/wms/count-executions?organizationId=org-001&environmentId=env-dev&scopeKind=site&scopeId=SITE-A" + forged);

        Assert.Equal(HttpStatusCode.OK, count.StatusCode);
        Assert.Equal("user-admin", wms.LastCountExecutionListRequest!.ActorPrincipalId);
        Assert.Equal(["SITE-A"], wms.LastCountExecutionListRequest.AuthorizedSiteCodes);
        Assert.Equal("site", wms.LastCountExecutionListRequest.ScopeKind);
        Assert.Equal("SITE-A", wms.LastCountExecutionListRequest.ScopeId);

        var outbound = await client.GetAsync(
            "/api/business-console/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev" + forged);

        Assert.Equal(HttpStatusCode.OK, outbound.StatusCode);
        Assert.Equal("user-admin", wms.LastOutboundRequest!.ActorPrincipalId);
        Assert.Equal(["SITE-A"], wms.LastOutboundRequest.AuthorizedSiteCodes);
        Assert.Equal("self", wms.LastOutboundRequest.ScopeKind);
        Assert.Equal("user-admin", wms.LastOutboundRequest.ScopeId);
        Assert.All(auth.Requirements, requirement => Assert.True(requirement.IncludePrincipalContext));
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("forged-self")]
    [InlineData("unauthorized-site")]
    [InlineData("permission-mismatch")]
    [InlineData("no-site-grant")]
    [InlineData("deny-all")]
    public async Task Wms_scoped_lists_fail_closed_before_downstream_when_scope_is_not_usable(string scenario)
    {
        var permission = BusinessGatewayPermissions.WmsReceiptsRead;
        var auth = scenario switch
        {
            "partial" or "forged-self" or "unauthorized-site" => ScopeAuth(
                [permission],
                new AuthorizationScopeGrant("role", "role-warehouse", "site", "SITE-A", [permission])),
            "permission-mismatch" => ScopeAuth(
                [permission],
                new AuthorizationScopeGrant(
                    "role",
                    "role-warehouse",
                    "site",
                    "SITE-A",
                    [BusinessGatewayPermissions.WmsShipmentsRead])),
            "deny-all" => new FakeBusinessGatewayAuthorizationClient(
                _ => true,
                dataScope: new AuthorizationDataScope([], [], [], DenyAll: true),
                scopeGrants:
                [
                    new AuthorizationScopeGrant("role", "role-warehouse", "site", "SITE-A", [permission]),
                ]),
            _ => ScopeAuth([permission]),
        };
        var requestUri = scenario switch
        {
            "partial" => "/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&scopeKind=work-pool",
            "forged-self" => "/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&scopeKind=self&scopeId=forged-user",
            "unauthorized-site" => "/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev&scopeKind=site&scopeId=SITE-B",
            _ => "/api/business-console/v1/wms/putaway-tasks?organizationId=org-001&environmentId=env-dev",
        };
        var wms = new RecordingWmsClient();
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(requestUri);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(wms.LastPutawayTaskRequest);
    }

    [Fact]
    public async Task Wms_manual_actions_attest_actor_and_scope_for_all_putaway_and_picking_transitions()
    {
        var wms = new RecordingWmsClient();
        var auth = ScopeAuth(
            [
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
            ],
            new AuthorizationScopeGrant(
                "role",
                "role-warehouse",
                "site",
                "SITE-A",
                [
                    BusinessGatewayPermissions.WmsReceiptsManage,
                    BusinessGatewayPermissions.WmsShipmentsManage,
                ]));
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var putawayStart = await PostTaskActionAsync(client, "putaway", "putaway-001", "start", "work-pool", "POOL-A");
        Assert.Equal(HttpStatusCode.OK, putawayStart.StatusCode);
        AssertTaskScope(wms.LastStartTaskRequest!, "putaway-001", "work-pool", "POOL-A");
        var putawayProgress = await PostTaskActionAsync(client, "putaway", "putaway-001", "progress", "work-pool", "POOL-A");
        Assert.Equal(HttpStatusCode.OK, putawayProgress.StatusCode);
        AssertTaskScope(wms.LastProgressTaskRequest!, "putaway-001", "work-pool", "POOL-A");
        var putawayException = await PostTaskActionAsync(client, "putaway", "putaway-001", "exception", "work-pool", "POOL-A");
        Assert.Equal(HttpStatusCode.OK, putawayException.StatusCode);
        AssertTaskScope(wms.LastExceptionTaskRequest!, "putaway-001", "work-pool", "POOL-A");
        var putawayComplete = await PostTaskActionAsync(client, "putaway", "putaway-001", "complete", "work-pool", "POOL-A");
        Assert.Equal(HttpStatusCode.OK, putawayComplete.StatusCode);
        AssertTaskScope(wms.LastCompleteTaskRequest!, "putaway-001", "work-pool", "POOL-A");

        var pickingStart = await PostTaskActionAsync(client, "picking", "picking-001", "start", "site", "SITE-A");
        Assert.Equal(HttpStatusCode.OK, pickingStart.StatusCode);
        AssertTaskScope(wms.LastStartTaskRequest!, "picking-001", "site", "SITE-A");
        var pickingProgress = await PostTaskActionAsync(client, "picking", "picking-001", "progress", "site", "SITE-A");
        Assert.Equal(HttpStatusCode.OK, pickingProgress.StatusCode);
        AssertTaskScope(wms.LastProgressTaskRequest!, "picking-001", "site", "SITE-A");
        var pickingException = await PostTaskActionAsync(client, "picking", "picking-001", "exception", "site", "SITE-A");
        Assert.Equal(HttpStatusCode.OK, pickingException.StatusCode);
        AssertTaskScope(wms.LastExceptionTaskRequest!, "picking-001", "site", "SITE-A");
        var pickingComplete = await PostTaskActionAsync(client, "picking", "picking-001", "complete", "site", "SITE-A");
        Assert.Equal(HttpStatusCode.OK, pickingComplete.StatusCode);
        AssertTaskScope(wms.LastCompleteTaskRequest!, "picking-001", "site", "SITE-A");

        Assert.Equal(
            [
                "start-putaway",
                "progress-putaway",
                "exception-putaway",
                "complete-putaway",
                "start-picking",
                "progress-picking",
                "exception-picking",
                "complete-picking",
            ],
            wms.Calls);
        Assert.All(auth.Requirements, requirement => Assert.True(requirement.IncludePrincipalContext));
        Assert.Equal(
            [
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsReceiptsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
                BusinessGatewayPermissions.WmsShipmentsManage,
            ],
            auth.Requirements.Select(requirement => requirement.PermissionCode).ToArray());
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.Equal("internal-test-token", wms.LastInternalToken);
    }

    [Fact]
    public async Task Wms_lifecycle_conflict_preserves_status_and_safe_code()
    {
        var wms = new RecordingWmsClient
        {
            CompleteInboundFailure = BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.Conflict,
                "lifecycle-conflict"),
        };
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-wms-receipts",
                    "site",
                    "SITE-001",
                    [BusinessGatewayPermissions.WmsReceiptsManage]),
            ]);
        await using var factory = CreateFactory(auth, services =>
        {
            services.RemoveAll<IBusinessWmsClient>();
            services.AddSingleton<IBusinessWmsClient>(wms);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/wms/inbound-orders/inbound-order-001/complete?organizationId=org-001&environmentId=env-dev",
            new
            {
                idempotencyKey = "complete-conflict-001",
                expectedVersion = 3,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("lifecycle-conflict", document.RootElement.GetProperty("message").GetString());
        Assert.Equal(3, wms.LastCompleteInboundRequest!.ExpectedVersion);
        Assert.Equal(["SITE-001"], wms.LastCompleteInboundRequest.AuthorizedSiteCodes);
        Assert.Equal(["complete-inbound"], wms.Calls);
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
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton<IBusinessMasterDataClient>(new RecordingMasterDataClient
                {
                    PrincipalWorkContext = WmsPrincipalWorkContext(),
                });
                configureServices?.Invoke(services);
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private static Task<HttpResponseMessage> PostTaskActionAsync(
        HttpClient client,
        string taskKind,
        string warehouseTaskId,
        string action,
        string scopeKind,
        string scopeId)
    {
        object body = action switch
        {
            "start" => new
            {
                warehouseTaskId = "forged-task-id",
                idempotencyKey = $"idem-{taskKind}-{action}",
                expectedVersion = 3,
                actorUserId = "forged-user",
                authorizedTeamIds = new[] { "forged-team" },
                authorizedSiteCodes = new[] { "forged-site" },
                organizationWideScope = true,
            },
            "progress" => new
            {
                warehouseTaskId = "forged-task-id",
                idempotencyKey = $"idem-{taskKind}-{action}",
                expectedVersion = 3,
                executedQuantity = 2m,
                actorUserId = "forged-user",
                authorizedTeamIds = new[] { "forged-team" },
                authorizedSiteCodes = new[] { "forged-site" },
                organizationWideScope = true,
            },
            "exception" => new
            {
                warehouseTaskId = "forged-task-id",
                idempotencyKey = $"idem-{taskKind}-{action}",
                expectedVersion = 3,
                exceptionCode = "LOCATION_BLOCKED",
                reason = "Location is blocked.",
                actorUserId = "forged-user",
                authorizedTeamIds = new[] { "forged-team" },
                authorizedSiteCodes = new[] { "forged-site" },
                organizationWideScope = true,
            },
            _ => new
            {
                warehouseTaskId = "forged-task-id",
                idempotencyKey = $"idem-{taskKind}-{action}",
                expectedVersion = 3,
                executedQuantity = 2m,
                differenceReason = "Verified difference.",
                actorUserId = "forged-user",
                authorizedTeamIds = new[] { "forged-team" },
                authorizedSiteCodes = new[] { "forged-site" },
                organizationWideScope = true,
            },
        };
        return client.PostAsJsonAsync(
            $"/api/business-console/v1/wms/{taskKind}-tasks/{warehouseTaskId}/{action}?organizationId=org-001&environmentId=env-dev&scopeKind={scopeKind}&scopeId={scopeId}",
            body);
    }

    private static void AssertTaskScope(
        object request,
        string expectedTaskId,
        string expectedScopeKind,
        string expectedScopeId,
        string siteCode = "SITE-A")
    {
        var values = request switch
        {
            BusinessWmsStartWarehouseTaskActionRequest x =>
                (x.WarehouseTaskId, x.ActorPrincipalId, x.AuthorizedSiteCodes, x.ScopeKind, x.ScopeId),
            BusinessWmsRecordWarehouseTaskProgressActionRequest x =>
                (x.WarehouseTaskId, x.ActorPrincipalId, x.AuthorizedSiteCodes, x.ScopeKind, x.ScopeId),
            BusinessWmsReportWarehouseTaskExceptionActionRequest x =>
                (x.WarehouseTaskId, x.ActorPrincipalId, x.AuthorizedSiteCodes, x.ScopeKind, x.ScopeId),
            BusinessWmsCompleteWarehouseTaskActionRequest x =>
                (x.WarehouseTaskId, x.ActorPrincipalId, x.AuthorizedSiteCodes, x.ScopeKind, x.ScopeId),
            _ => throw new InvalidOperationException($"Unexpected task request {request.GetType().Name}."),
        };
        Assert.Equal(expectedTaskId, values.WarehouseTaskId);
        Assert.Equal("user-admin", values.ActorPrincipalId);
        Assert.Equal([siteCode], values.AuthorizedSiteCodes);
        Assert.Equal(expectedScopeKind, values.ScopeKind);
        Assert.Equal(expectedScopeId, values.ScopeId);
    }

    private static void AssertTrustedAssignment(
        object request,
        string expectedResourceId,
        string expectedIdempotencyKey)
    {
        var values = request switch
        {
            BusinessWmsAssignInboundOrderRequest x =>
                (
                    x.InboundOrderId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.AssignerPrincipalId,
                    x.AuthorizedSiteCodes,
                    x.PoolCode,
                    x.OperatorPrincipalId,
                    x.IdempotencyKey,
                    x.ExpectedVersion),
            BusinessWmsAssignPutawayTaskRequest x =>
                (
                    x.WarehouseTaskId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.AssignerPrincipalId,
                    x.AuthorizedSiteCodes,
                    x.PoolCode,
                    x.OperatorPrincipalId,
                    x.IdempotencyKey,
                    x.ExpectedVersion),
            BusinessWmsAssignOutboundOrderRequest x =>
                (
                    x.OutboundOrderId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.AssignerPrincipalId,
                    x.AuthorizedSiteCodes,
                    x.PoolCode,
                    x.OperatorPrincipalId,
                    x.IdempotencyKey,
                    x.ExpectedVersion),
            BusinessWmsAssignPickingTaskRequest x =>
                (
                    x.WarehouseTaskId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.AssignerPrincipalId,
                    x.AuthorizedSiteCodes,
                    x.PoolCode,
                    x.OperatorPrincipalId,
                    x.IdempotencyKey,
                    x.ExpectedVersion),
            BusinessWmsAssignCountExecutionRequest x =>
                (
                    x.CountExecutionId,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.AssignerPrincipalId,
                    x.AuthorizedSiteCodes,
                    x.PoolCode,
                    x.OperatorPrincipalId,
                    x.IdempotencyKey,
                    x.ExpectedVersion),
            _ => throw new InvalidOperationException(
                $"Unexpected assignment request {request.GetType().Name}."),
        };
        Assert.Equal(expectedResourceId, values.Item1);
        Assert.Equal("org-001", values.OrganizationId);
        Assert.Equal("env-dev", values.EnvironmentId);
        Assert.Equal("user-admin", values.AssignerPrincipalId);
        Assert.Equal(["SITE-A"], values.AuthorizedSiteCodes);
        Assert.Equal("POOL-WAREHOUSE", values.PoolCode);
        Assert.Equal("user-emp-049", values.OperatorPrincipalId);
        Assert.Equal(expectedIdempotencyKey, values.IdempotencyKey);
        Assert.Equal(3, values.ExpectedVersion);
    }

    private static FakeBusinessGatewayAuthorizationClient ScopeAuth(
        string[] permissionCodes,
        params AuthorizationScopeGrant[] grants)
    {
        var allowedPermissions = permissionCodes.ToHashSet(StringComparer.Ordinal);
        return new FakeBusinessGatewayAuthorizationClient(
            requirement => allowedPermissions.Contains(requirement.PermissionCode),
            scopeGrants: grants);
    }

    private static FakeBusinessGatewayAuthorizationClient OrganizationScopeAuth(params string[] permissionCodes)
        => ScopeAuth(
            permissionCodes,
            new AuthorizationScopeGrant(
                "role",
                "role-wms",
                "site",
                "S1",
                permissionCodes));

    private static BusinessMasterDataPrincipalWorkContextResponse WmsPrincipalWorkContext() =>
        new(
            "resolved",
            new BusinessMasterDataWorkContextWorker(
                "worker-001",
                "user-admin",
                "EMP-001",
                "Admin",
                null,
                null,
                null,
                "active"),
            [new BusinessMasterDataWorkContextTeam("TEAM-A", "Team A", false, "WS-A", "SHIFT-A")],
            [new BusinessMasterDataWorkContextCoveredWorkCenter("WC-A", "Work center A", "WS-A", "assigned")],
            [new BusinessMasterDataWorkContextReference("WS-A", "Workshop A")],
            [],
            [new BusinessMasterDataWorkContextReference("SITE-A", "Site A")],
            [
                Candidate("self", "user-admin", "Admin"),
                Candidate("team", "TEAM-A", "Team A"),
                Candidate("work-center", "WC-A", "Work center A"),
                Candidate("workshop", "WS-A", "Workshop A"),
                Candidate("site", "SITE-A", "Site A"),
                Candidate("organization", "org-001", "Organization"),
            ],
            ["self", "team", "work-center", "workshop", "site", "organization"],
            []);

    private static BusinessMasterDataWorkContextCandidateScope Candidate(
        string kind,
        string id,
        string displayName) =>
        new(
            kind,
            id,
            displayName,
            "test",
            string.Equals(kind, "organization", StringComparison.Ordinal)
                ? []
                : [new BusinessMasterDataWorkContextScopeAncestor("organization", "org-001")]);

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

    private static BusinessWmsCompleteInboundOrderRequest ValidCompleteInboundRequest() =>
        new(
            "inbound-order-001",
            "org-001",
            "env-dev",
            "user-admin",
            ["S1"],
            "self",
            "user-admin",
            1,
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

    private static BusinessWmsCompleteOutboundOrderRequest ValidCompleteOutboundRequest() =>
        new(
            "outbound-order-001",
            "org-001",
            "env-dev",
            "user-admin",
            ["S1"],
            "self",
            "user-admin",
            1,
            "PACK-001",
            true,
            "complete-out-001");

    private static BusinessConsoleRetryWmsOutboundInventoryPostingRequest ValidRetryOutboundRequest() =>
        new("outbound-order-001", "org-001", "env-dev", "retry-out-001");

    private static BusinessConsoleCreateWmsCountExecutionRequest ValidCreateCountRequest() =>
        new("org-001", "env-dev", "COUNT-001", "SKU-001", "EA", "S1", "BIN-01", 1);

    private static BusinessWmsCompleteCountExecutionRequest ValidCompleteCountRequest() =>
        new(
            "count-execution-001",
            "org-001",
            "env-dev",
            "user-admin",
            ["S1"],
            "self",
            "user-admin",
            1,
            1,
            "complete-count-001");

    private static BusinessWmsDispatchWcsTaskRequest ValidDispatchWcsRequest() =>
        new(
            "warehouse-task-001",
            "org-001",
            "env-dev",
            "user-admin",
            ["SITE-A"],
            3,
            "agv",
            "EXT-001",
            "{}");

    private static BusinessConsoleFailWmsWcsTaskRequest ValidFailWcsRequest() =>
        new("EXT-001", "org-001", "env-dev", "PLC_TIMEOUT", "PLC did not acknowledge.");

    private static BusinessConsoleCompleteWmsWcsTaskRequest ValidCompleteWcsRequest() =>
        new("EXT-001", "org-001", "env-dev", "{}");

    private static BusinessWmsStartWarehouseTaskActionRequest ValidStartTaskAction(
        string warehouseTaskId,
        string? poolCode = null,
        string? siteCode = null) =>
        new(
            warehouseTaskId,
            "org-001",
            "env-dev",
            "user-admin",
            $"idem-{warehouseTaskId}-start",
            3,
            [siteCode ?? "SITE-A"],
            poolCode is null ? siteCode is null ? "self" : "site" : "work-pool",
            poolCode ?? siteCode ?? "user-admin");

    private static BusinessWmsRecordWarehouseTaskProgressActionRequest ValidProgressTaskAction(
        string warehouseTaskId,
        string? poolCode = null,
        string? siteCode = null) =>
        new(
            warehouseTaskId,
            "org-001",
            "env-dev",
            "user-admin",
            $"idem-{warehouseTaskId}-progress",
            3,
            1m,
            [siteCode ?? "SITE-A"],
            poolCode is null ? siteCode is null ? "self" : "site" : "work-pool",
            poolCode ?? siteCode ?? "user-admin");

    private static BusinessWmsReportWarehouseTaskExceptionActionRequest ValidExceptionTaskAction(
        string warehouseTaskId,
        string? poolCode = null,
        string? siteCode = null) =>
        new(
            warehouseTaskId,
            "org-001",
            "env-dev",
            "user-admin",
            $"idem-{warehouseTaskId}-exception",
            3,
            "LOCATION_BLOCKED",
            "Location is blocked.",
            [siteCode ?? "SITE-A"],
            poolCode is null ? siteCode is null ? "self" : "site" : "work-pool",
            poolCode ?? siteCode ?? "user-admin");

    private static BusinessWmsCompleteWarehouseTaskActionRequest ValidCompleteTaskAction(
        string warehouseTaskId,
        string? poolCode = null,
        string? siteCode = null) =>
        new(
            warehouseTaskId,
            "org-001",
            "env-dev",
            "user-admin",
            $"idem-{warehouseTaskId}-complete",
            3,
            1m,
            [siteCode ?? "SITE-A"],
            poolCode is null ? siteCode is null ? "self" : "site" : "work-pool",
            poolCode ?? siteCode ?? "user-admin",
            "Verified difference.");

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

    public BusinessWmsWorkScopeCatalogRequest? LastWorkScopeCatalogRequest { get; private set; }

    public object? LastAssignmentRequest { get; private set; }

    public BusinessWmsScopedListRequest? LastInboundRequest { get; private set; }

    public string? LastInboundOrderId { get; private set; }

    public BusinessWmsScopedListRequest? LastOutboundRequest { get; private set; }

    public string? LastOutboundOrderId { get; private set; }

    public BusinessWmsWarehouseTaskListRequest? LastPutawayTaskRequest { get; private set; }

    public BusinessWmsWarehouseTaskListRequest? LastPickingTaskRequest { get; private set; }

    public BusinessWmsCountExecutionListRequest? LastCountExecutionListRequest { get; private set; }

    public BusinessWmsStartWarehouseTaskActionRequest? LastStartTaskRequest { get; private set; }

    public BusinessWmsRecordWarehouseTaskProgressActionRequest? LastProgressTaskRequest { get; private set; }

    public BusinessWmsReportWarehouseTaskExceptionActionRequest? LastExceptionTaskRequest { get; private set; }

    public BusinessWmsCompleteWarehouseTaskActionRequest? LastCompleteTaskRequest { get; private set; }

    public BusinessConsoleWmsWcsTaskListRequest? LastWcsTaskRequest { get; private set; }

    public BusinessConsoleWmsReceivingQualityGateListRequest? LastReceivingQualityGateRequest { get; private set; }

    public BusinessConsoleWmsListRequest? LastSupplierReturnRequest { get; private set; }

    public BusinessConsoleCreateWmsInboundOrderRequest? LastCreateInboundRequest { get; private set; }

    public BusinessConsoleCreateWmsPutawayTaskRequest? LastCreatePutawayRequest { get; private set; }

    public BusinessWmsCompleteInboundOrderRequest? LastCompleteInboundRequest { get; private set; }

    public BusinessServiceProxyException? CompleteInboundFailure { get; init; }

    public BusinessServiceProxyException? AssignmentFailure { get; init; }

    public BusinessConsoleCreateWmsOutboundOrderRequest? LastCreateOutboundRequest { get; private set; }

    public BusinessConsoleCreateWmsPickingTaskRequest? LastCreatePickingRequest { get; private set; }

    public BusinessWmsCompleteOutboundOrderRequest? LastCompleteOutboundRequest { get; private set; }

    public BusinessConsoleRetryWmsOutboundInventoryPostingRequest? LastRetryOutboundRequest { get; private set; }

    public BusinessConsoleCreateWmsCountExecutionRequest? LastCreateCountRequest { get; private set; }

    public BusinessWmsCompleteCountExecutionRequest? LastCompleteCountRequest { get; private set; }

    public BusinessWmsDispatchWcsTaskRequest? LastDispatchWcsRequest { get; private set; }

    public BusinessConsoleFailWmsWcsTaskRequest? LastFailWcsRequest { get; private set; }

    public BusinessConsoleCompleteWmsWcsTaskRequest? LastCompleteWcsRequest { get; private set; }

    public Task<BusinessConsoleWmsWorkScopeCatalog> GetReceiptWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        RecordWorkScopes(internalBearerToken, request, "receipt-scopes");

    public Task<BusinessConsoleWmsWorkScopeCatalog> GetShipmentWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        RecordWorkScopes(internalBearerToken, request, "shipment-scopes");

    public Task<BusinessConsoleWmsWorkScopeCatalog> GetCountWorkScopesAsync(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        CancellationToken cancellationToken) =>
        RecordWorkScopes(internalBearerToken, request, "count-scopes");

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

    public Task<BusinessConsoleWmsAssignmentResult> AssignInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessWmsAssignInboundOrderRequest request,
        CancellationToken cancellationToken) =>
        RecordAssignment(internalBearerToken, request, "inbound-order", inboundOrderId);

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

    public Task<BusinessConsoleWmsAssignmentResult> AssignPutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsAssignPutawayTaskRequest request,
        CancellationToken cancellationToken) =>
        RecordAssignment(internalBearerToken, request, "putaway-task", warehouseTaskId);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteInboundOrderAsync(
        string internalBearerToken,
        string inboundOrderId,
        BusinessWmsCompleteInboundOrderRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteInboundRequest = request;
        Calls.Add("complete-inbound");
        if (CompleteInboundFailure is not null)
        {
            throw CompleteInboundFailure;
        }

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

    public Task<BusinessConsoleWmsAssignmentResult> AssignOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessWmsAssignOutboundOrderRequest request,
        CancellationToken cancellationToken) =>
        RecordAssignment(internalBearerToken, request, "outbound-order", outboundOrderId);

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

    public Task<BusinessConsoleWmsAssignmentResult> AssignPickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsAssignPickingTaskRequest request,
        CancellationToken cancellationToken) =>
        RecordAssignment(internalBearerToken, request, "picking-task", warehouseTaskId);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteOutboundOrderAsync(
        string internalBearerToken,
        string outboundOrderId,
        BusinessWmsCompleteOutboundOrderRequest request,
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

    public Task<BusinessConsoleWmsAssignmentResult> AssignCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessWmsAssignCountExecutionRequest request,
        CancellationToken cancellationToken) =>
        RecordAssignment(internalBearerToken, request, "count-execution", countExecutionId);

    public Task<BusinessConsoleCompleteWmsMovementResponse> CompleteCountExecutionAsync(
        string internalBearerToken,
        string countExecutionId,
        BusinessWmsCompleteCountExecutionRequest request,
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
        BusinessWmsDispatchWcsTaskRequest request,
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
        BusinessWmsScopedListRequest request,
        string? inboundOrderId,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastInboundRequest = request;
        LastInboundOrderId = inboundOrderId;
        return Task.FromResult(new BusinessConsoleWmsInboundOrderListResponse(
        [
            new BusinessConsoleWmsInboundOrderItem(
                "inbound-order-001",
                "IN-001",
                "Created",
                DateTime.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                "pending",
                false,
                "S1",
                "user-admin",
                "POOL-WAREHOUSE",
                3),
        ],
        47,
        null,
        "unsupported"));
    }

    public Task<BusinessConsoleWmsOutboundOrderListResponse> ListOutboundOrdersAsync(
        string internalBearerToken,
        BusinessWmsScopedListRequest request,
        string? outboundOrderId,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastOutboundRequest = request;
        LastOutboundOrderId = outboundOrderId;
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
                null,
                "user-admin",
                "POOL-WAREHOUSE",
                3),
        ],
        31));
    }

    public Task<BusinessConsoleWmsWarehouseTaskListResponse> ListPutawayTasksAsync(
        string internalBearerToken,
        BusinessWmsWarehouseTaskListRequest request,
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
        BusinessWmsWarehouseTaskListRequest request,
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

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> StartPutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsStartWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        RecordStart(internalBearerToken, request, "start-putaway");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordPutawayTaskProgressAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        CancellationToken cancellationToken) =>
        RecordProgress(internalBearerToken, request, "progress-putaway");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> ReportPutawayTaskExceptionAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        CancellationToken cancellationToken) =>
        RecordException(internalBearerToken, request, "exception-putaway");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> CompletePutawayTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        RecordComplete(internalBearerToken, request, "complete-putaway");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> StartPickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsStartWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        RecordStart(internalBearerToken, request, "start-picking");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordPickingTaskProgressAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        CancellationToken cancellationToken) =>
        RecordProgress(internalBearerToken, request, "progress-picking");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> ReportPickingTaskExceptionAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        CancellationToken cancellationToken) =>
        RecordException(internalBearerToken, request, "exception-picking");

    public Task<BusinessConsoleWmsWarehouseTaskActionResult> CompletePickingTaskAsync(
        string internalBearerToken,
        string warehouseTaskId,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        CancellationToken cancellationToken) =>
        RecordComplete(internalBearerToken, request, "complete-picking");

    public Task<BusinessConsoleWmsCountExecutionListResponse> ListCountExecutionsAsync(
        string internalBearerToken,
        BusinessWmsCountExecutionListRequest request,
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
                null,
                "Failed",
                "NEGATIVE_ON_HAND",
                "Stock movement would make on-hand quantity negative.",
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

    private Task<BusinessConsoleWmsWorkScopeCatalog> RecordWorkScopes(
        string internalBearerToken,
        BusinessWmsWorkScopeCatalogRequest request,
        string call)
    {
        LastInternalToken = internalBearerToken;
        LastWorkScopeCatalogRequest = request;
        Calls.Add(call);
        return Task.FromResult(new BusinessConsoleWmsWorkScopeCatalog(
            request.ActorPrincipalId,
            [
                new(
                    "self",
                    request.ActorPrincipalId,
                    "我的任务",
                    null,
                    null),
                new(
                    "work-pool",
                    "POOL-WAREHOUSE",
                    "仓储作业池",
                    "SITE-A",
                    "POOL-WAREHOUSE"),
                new(
                    "site",
                    "SITE-A",
                    "SITE-A",
                    "SITE-A",
                    null),
            ]));
    }

    private Task<BusinessConsoleWmsAssignmentResult> RecordAssignment(
        string internalBearerToken,
        object request,
        string resourceCategory,
        string resourceId)
    {
        LastInternalToken = internalBearerToken;
        LastAssignmentRequest = request;
        Calls.Add($"assign-{resourceCategory}");
        if (AssignmentFailure is not null)
        {
            throw AssignmentFailure;
        }

        var values = request switch
        {
            BusinessWmsAssignInboundOrderRequest x =>
                (x.PoolCode, x.OperatorPrincipalId, x.AssignerPrincipalId, x.ExpectedVersion),
            BusinessWmsAssignPutawayTaskRequest x =>
                (x.PoolCode, x.OperatorPrincipalId, x.AssignerPrincipalId, x.ExpectedVersion),
            BusinessWmsAssignOutboundOrderRequest x =>
                (x.PoolCode, x.OperatorPrincipalId, x.AssignerPrincipalId, x.ExpectedVersion),
            BusinessWmsAssignPickingTaskRequest x =>
                (x.PoolCode, x.OperatorPrincipalId, x.AssignerPrincipalId, x.ExpectedVersion),
            BusinessWmsAssignCountExecutionRequest x =>
                (x.PoolCode, x.OperatorPrincipalId, x.AssignerPrincipalId, x.ExpectedVersion),
            _ => throw new InvalidOperationException(
                $"Unexpected assignment request {request.GetType().Name}."),
        };
        return Task.FromResult(new BusinessConsoleWmsAssignmentResult(
            resourceCategory,
            resourceId,
            "SITE-A",
            values.PoolCode,
            values.OperatorPrincipalId,
            values.AssignerPrincipalId,
            values.ExpectedVersion + 1));
    }

    private Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordStart(
        string internalBearerToken,
        BusinessWmsStartWarehouseTaskActionRequest request,
        string call)
    {
        LastInternalToken = internalBearerToken;
        LastStartTaskRequest = request;
        Calls.Add(call);
        return Task.FromResult(ActionResult(request.WarehouseTaskId, "InProgress", request.ExpectedVersion + 1));
    }

    private Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordProgress(
        string internalBearerToken,
        BusinessWmsRecordWarehouseTaskProgressActionRequest request,
        string call)
    {
        LastInternalToken = internalBearerToken;
        LastProgressTaskRequest = request;
        Calls.Add(call);
        return Task.FromResult(ActionResult(request.WarehouseTaskId, "InProgress", request.ExpectedVersion + 1));
    }

    private Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordException(
        string internalBearerToken,
        BusinessWmsReportWarehouseTaskExceptionActionRequest request,
        string call)
    {
        LastInternalToken = internalBearerToken;
        LastExceptionTaskRequest = request;
        Calls.Add(call);
        return Task.FromResult(ActionResult(request.WarehouseTaskId, "Exception", request.ExpectedVersion + 1));
    }

    private Task<BusinessConsoleWmsWarehouseTaskActionResult> RecordComplete(
        string internalBearerToken,
        BusinessWmsCompleteWarehouseTaskActionRequest request,
        string call)
    {
        LastInternalToken = internalBearerToken;
        LastCompleteTaskRequest = request;
        Calls.Add(call);
        return Task.FromResult(ActionResult(request.WarehouseTaskId, "Completed", request.ExpectedVersion + 1));
    }

    private static BusinessConsoleWmsWarehouseTaskActionResult ActionResult(
        string warehouseTaskId,
        string status,
        long version) =>
        new(warehouseTaskId, "Putaway", status, version, 1m, 0m, [], []);
}
