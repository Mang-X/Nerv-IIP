extern alias BusinessGateway;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using BusinessGateway::Nerv.IIP.BusinessGateway.Web.Application.Auth;
using BusinessGateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;
using GatewayProgram = BusinessGateway::Program;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class NcrReworkCostClosurePostgresRedisAcceptanceTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private const string OtherOrganizationId = "org-2813-other";
    private const string PrincipalId = "user-admin";
    private const string SkuCode = "SKU-DEMO-001";
    private const string RawMaterialSkuCode = "SKU-DEMO-RM-001";
    private const string WorkCenterId = "WC-CNC-DEMO";
    private const string SourceWorkOrderId = "WO-MAN2813-SOURCE";
    private const string SourceOperationTaskId = "OP-MAN2813-SOURCE";
    private const string SourceLotNo = "LOT-MAN2813-SOURCE";
    private const string SourceSerialNo = "SN-MAN2813-SOURCE";
    private const decimal PlannedQuantity = 10m;
    private const decimal ReworkQuantity = 2m;
    private const decimal ExpectedReworkLaborCost = 120m;

    [RealNcrReworkFullChainFact]
    public async Task Public_ncr_rework_closes_one_traceable_work_order_and_independent_erp_cost()
    {
        var endpoints = ScenarioEndpoints.FromEnvironment();
        using var approval = InternalClient(endpoints.Approval, endpoints.InternalToken);
        using var quality = InternalClient(endpoints.Quality, endpoints.InternalToken);
        using var productEngineering = InternalClient(endpoints.ProductEngineering, endpoints.InternalToken);
        using var inventory = InternalClient(endpoints.Inventory, endpoints.InternalToken);
        using var erp = InternalClient(endpoints.Erp, endpoints.InternalToken);

        var productionVersion = await GetDataAsync(productEngineering, HttpMethod.Get,
            $"/api/business/v1/engineering/production-versions/resolve?organizationId={OrganizationId}" +
            $"&environmentId={EnvironmentId}&skuCode={SkuCode}&effectiveDate={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&lotSize={PlannedQuantity}");
        var productionVersionId = productionVersion.GetProperty("productionVersionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(productionVersionId));

        await PostDataAsync(inventory, "/api/inventory/v1/movements", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            movementType = "inbound",
            sourceService = "full-chain-2813",
            sourceDocumentId = "STOCK-MAN2813",
            sourceDocumentLineId = "10",
            idempotencyKey = "man2813-stock",
            skuCode = RawMaterialSkuCode,
            uomCode = "pcs",
            siteCode = "production",
            locationCode = "LINE-SIDE-MAN2813",
            lotNo = "LOT-RM-MAN2813",
            serialNo = (string?)null,
            qualityStatus = "unrestricted",
            ownerType = "production",
            ownerId = (string?)null,
            quantity = 100m,
            unitCost = 1m,
            reservationId = (string?)null,
        });

        await using var gateway = CreateGateway(endpoints);
        using var browser = gateway.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PublicGatewayToken.ValidAccessToken(OrganizationId, EnvironmentId));

        var sourceWorkOrder = await Eventually.WaitAsync(
            condition: "MES accepts the run-scoped source work order after the real ProductEngineering projection converges",
            observe: async token => await TryPostDataAsync(browser, "/api/business-console/v1/mes/work-orders/rush", new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                workOrderId = SourceWorkOrderId,
                skuId = SkuCode,
                productionVersionId,
                quantity = PlannedQuantity,
                dueUtc = DateTimeOffset.UtcNow.AddDays(1),
                workCenterId = WorkCenterId,
                operationTaskId = SourceOperationTaskId,
                operationSequence = 10,
                durationMinutes = 60,
                idempotencyKey = "man2813-source-work-order",
            }, token),
            isSatisfied: result => result.IsSuccess,
            describe: result => result.Diagnostic,
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(500),
                SensitiveValues: [endpoints.InternalToken]));
        Assert.Equal(SourceWorkOrderId, sourceWorkOrder.Data.GetProperty("workOrderId").GetString());

        var defect = await PostDataAsync(browser, "/api/business-console/v2/mes/defects", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            workOrderId = SourceWorkOrderId,
            operationTaskId = SourceOperationTaskId,
            defectCode = "DEF-MAN2813",
            quantity = 2m,
            recordedAtUtc = DateTimeOffset.UtcNow,
            idempotencyKey = "man2813-source-defect",
            scopeKind = "organization",
            scopeId = OrganizationId,
        });
        var defectNo = defect.GetProperty("downstreamDocumentId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(defectNo));

        var ncr = await PostDataAsync(quality, "/api/business/v1/quality/ncrs", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            sourceType = "in-process",
            sourceDocumentId = defectNo,
            skuCode = SkuCode,
            defectQuantity = ReworkQuantity,
            defectReason = "MAN-2813 返工闭环",
            batchNo = SourceLotNo,
            serialNo = SourceSerialNo,
            attachmentFileIds = new[] { "file-man2813-ncr" },
        });
        var ncrId = StrongId(ncr.GetProperty("ncrId"));
        var ncrCode = ncr.GetProperty("ncrCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ncrId));
        Assert.False(string.IsNullOrWhiteSpace(ncrCode));

        var templateCode = $"APT-MAN2813-{ncrId[..8]}";
        await PostDataAsync(approval, "/api/business/v1/approvals/templates", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            templateCode,
            documentType = "ncr-disposition",
            version = 1,
            isActive = true,
            steps = new[]
            {
                new
                {
                    stepNo = 1,
                    stepName = "质量主管批准",
                    parallelGroupKey = (string?)null,
                    approverType = "user",
                    approverRef = PrincipalId,
                    dueInHours = (int?)null,
                    completionPolicy = "all",
                },
            },
        });
        var chain = await PostDataAsync(approval, "/api/business/v1/approvals/chains", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            templateCode,
            sourceService = "business-quality",
            documentType = "ncr-disposition",
            documentId = ncrCode,
            documentLineId = (string?)null,
            startedBy = PrincipalId,
        });
        var chainId = StrongId(chain.GetProperty("chainId"));
        await PostDataAsync(approval, $"/api/business/v1/approvals/chains/{chainId}/steps/1/resolve", new
        {
            chainId,
            stepNo = 1,
            actorType = "user",
            actorRef = PrincipalId,
            decision = "approve",
            comment = "MAN-2813 返工批准",
        });

        var dispositionBody = new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            dispositionType = "rework",
            dispositionApprovalChainId = chainId,
            attachmentFileIds = new[] { "file-man2813-disposition" },
            mrbReviews = new[]
            {
                new
                {
                    reviewerId = PrincipalId,
                    decision = "approved",
                    comment = "允许返工",
                    reviewedAtUtc = DateTimeOffset.UtcNow,
                },
            },
            idempotencyKey = "man2813-ncr-rework",
        };
        await PostDataAsync(browser,
            $"/api/business-console/v1/quality/ncrs/{ncrId}/disposition",
            dispositionBody);
        await PostDataAsync(browser,
            $"/api/business-console/v1/quality/ncrs/{ncrId}/disposition",
            dispositionBody);

        var closure = await Eventually.WaitAsync(
            condition: "one MES rework work order is bound back to the NCR through real Redis CAP",
            observe: async token =>
            {
                var ncrReadback = await GetDataAsync(browser,
                    $"/api/business-console/v1/quality/ncrs/{ncrId}?organizationId={OrganizationId}&environmentId={EnvironmentId}", token);
                var workOrders = await GetDataAsync(browser,
                    $"/api/business-console/v1/mes/work-orders?organizationId={OrganizationId}&environmentId={EnvironmentId}" +
                    $"&scopeKind=organization&scopeId={OrganizationId}&take=100", token);
                return new { Ncr = ncrReadback, WorkOrders = workOrders };
            },
            isSatisfied: state =>
                state.Ncr.GetProperty("reworkWorkOrderCreationStatus").GetString() == "created" &&
                !string.IsNullOrWhiteSpace(state.Ncr.GetProperty("reworkWorkOrderId").GetString()) &&
                state.WorkOrders.GetProperty("total").GetInt32() == 2,
            describe: state => JsonSerializer.Serialize(new
            {
                ncrStatus = state.Ncr.GetProperty("reworkWorkOrderCreationStatus").GetString(),
                reworkWorkOrderId = state.Ncr.GetProperty("reworkWorkOrderId").GetString(),
                workOrderTotal = state.WorkOrders.GetProperty("total").GetInt32(),
            }),
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(500),
                SensitiveValues: [endpoints.InternalToken]));

        var reworkWorkOrderId = closure.Ncr.GetProperty("reworkWorkOrderId").GetString();
        Assert.NotNull(reworkWorkOrderId);
        var rows = closure.WorkOrders.GetProperty("items").EnumerateArray().ToArray();
        var rework = Assert.Single(rows, row => row.GetProperty("workOrderType").GetString() == "rework");
        Assert.Equal(reworkWorkOrderId, rework.GetProperty("workOrderId").GetString());
        Assert.Equal(SourceWorkOrderId, rework.GetProperty("sourceWorkOrderId").GetString());
        Assert.Equal(ncrId, rework.GetProperty("sourceNcrId").GetString());
        Assert.Equal(ncrCode, rework.GetProperty("sourceNcrCode").GetString());
        Assert.Equal(ReworkQuantity, rework.GetProperty("quantity").GetDecimal());
        var reworkTask = Assert.Single(rework.GetProperty("operationTasks").EnumerateArray());
        var reworkOperationTaskId = reworkTask.GetProperty("operationTaskId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reworkOperationTaskId));
        Assert.Equal(10, reworkTask.GetProperty("operationSequence").GetInt32());
        Assert.Equal(WorkCenterId, reworkTask.GetProperty("workCenterId").GetString());
        Assert.Equal(TimeSpan.FromHours(1).Ticks, reworkTask.GetProperty("durationTicks").GetInt64());

        var changedDisposition = await browser.PostAsJsonAsync(
            $"/api/business-console/v1/quality/ncrs/{ncrId}/disposition",
            new
            {
                dispositionBody.organizationId,
                dispositionBody.environmentId,
                dispositionType = "scrap",
                dispositionBody.dispositionApprovalChainId,
                dispositionBody.attachmentFileIds,
                dispositionBody.mrbReviews,
                dispositionBody.idempotencyKey,
            });
        Assert.Equal(HttpStatusCode.Conflict, changedDisposition.StatusCode);

        using var otherScopeBrowser = gateway.CreateClient();
        otherScopeBrowser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PublicGatewayToken.ValidAccessToken(OtherOrganizationId, EnvironmentId));
        var otherScopeWorkOrders = await GetDataAsync(otherScopeBrowser,
            $"/api/business-console/v1/mes/work-orders?organizationId={OtherOrganizationId}&environmentId={EnvironmentId}" +
            $"&scopeKind=organization&scopeId={OtherOrganizationId}&take=100");
        Assert.Equal(0, otherScopeWorkOrders.GetProperty("total").GetInt32());
        Assert.Empty(otherScopeWorkOrders.GetProperty("items").EnumerateArray());

        await PostDataAsync(erp, "/api/business/v1/erp/finance/work-center-cost-rates", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            workCenterId = WorkCenterId,
            hourlyRate = 120m,
            currencyCode = "CNY",
            effectiveFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
            effectiveToUtc = (DateTimeOffset?)null,
            reason = "MAN-2813 independent literal rate",
        });

        await PostDataAsync(browser,
            $"/api/business-console/v1/mes/operation-tasks/{reworkOperationTaskId}/start",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                operationTaskId = reworkOperationTaskId,
                reasonCode = "rework",
                idempotencyKey = "man2813-rework-start",
                scopeKind = "organization",
                scopeId = OrganizationId,
            });
        var report = await PostDataAsync(browser, "/api/business-console/v1/mes/production-reports", new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            workOrderId = reworkWorkOrderId,
            operationTaskId = reworkOperationTaskId,
            goodQuantity = ReworkQuantity,
            scrapQuantity = 0m,
            completesOperation = false,
            reportedAtUtc = DateTimeOffset.UtcNow,
            idempotencyKey = "man2813-rework-report",
            scopeKind = "organization",
            scopeId = OrganizationId,
            producedLotNo = "LOT-MAN2813-REWORK-OUTPUT",
            serialNo = "SN-MAN2813-REWORK-OUTPUT",
        });
        var reportNo = report.GetProperty("reportNo").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reportNo));

        var costAndTrace = await Eventually.WaitAsync(
            condition: "ERP independently reads one 120 CNY rework labor cost and MES exposes exact rework lineage",
            observe: async token => new
            {
                ReworkCost = await GetDataAsync(erp, HttpMethod.Get,
                    $"/api/business/v1/erp/finance/work-order-costs?organizationId={OrganizationId}&environmentId={EnvironmentId}&workOrderId={reworkWorkOrderId}", token),
                SourceCost = await GetDataAsync(erp, HttpMethod.Get,
                    $"/api/business/v1/erp/finance/work-order-costs?organizationId={OrganizationId}&environmentId={EnvironmentId}&workOrderId={SourceWorkOrderId}", token),
                OtherScopeCost = await GetDataAsync(erp, HttpMethod.Get,
                    $"/api/business/v1/erp/finance/work-order-costs?organizationId={OtherOrganizationId}&environmentId={EnvironmentId}&workOrderId={reworkWorkOrderId}", token),
                Trace = await GetDataAsync(browser, HttpMethod.Get,
                    $"/api/business-console/v1/mes/traceability/work-orders/{reworkWorkOrderId}?organizationId={OrganizationId}&environmentId={EnvironmentId}", token),
            },
            isSatisfied: state =>
                state.ReworkCost.GetProperty("total").GetInt32() == 1 &&
                state.ReworkCost.GetProperty("reworkCostTotal").GetDecimal() == ExpectedReworkLaborCost &&
                state.SourceCost.GetProperty("total").GetInt32() == 0 &&
                state.OtherScopeCost.GetProperty("total").GetInt32() == 0 &&
                HasEdge(state.Trace, ncrId, reworkWorkOrderId!, "created-rework-work-order") &&
                HasEdge(state.Trace, reportNo!, "LOT-MAN2813-REWORK-OUTPUT", "produced-lot"),
            describe: state => JsonSerializer.Serialize(new
            {
                reworkTotal = state.ReworkCost.GetProperty("total").GetInt32(),
                reworkCost = state.ReworkCost.GetProperty("reworkCostTotal").GetDecimal(),
                sourceTotal = state.SourceCost.GetProperty("total").GetInt32(),
                otherScopeTotal = state.OtherScopeCost.GetProperty("total").GetInt32(),
                traceEdges = state.Trace.GetProperty("edges").GetArrayLength(),
            }),
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(500),
                SensitiveValues: [endpoints.InternalToken]));

        Assert.True(HasEdge(costAndTrace.Trace, SourceWorkOrderId, ncrId, "raised-ncr"));
        var sourceLotNode = Assert.Single(costAndTrace.Trace.GetProperty("nodes").EnumerateArray(), node =>
            node.GetProperty("nodeType").GetString() == "ProducedLot" &&
            node.GetProperty("displayName").GetString() == SourceLotNo &&
            node.GetProperty("status").GetString() == "Source");
        var sourceSerialNode = Assert.Single(costAndTrace.Trace.GetProperty("nodes").EnumerateArray(), node =>
            node.GetProperty("nodeType").GetString() == "Serial" &&
            node.GetProperty("displayName").GetString() == SourceSerialNo &&
            node.GetProperty("status").GetString() == "Source");
        Assert.True(HasEdge(costAndTrace.Trace, sourceLotNode.GetProperty("nodeId").GetString()!, ncrId, "identified-in-ncr"));
        Assert.True(HasEdge(costAndTrace.Trace, sourceSerialNode.GetProperty("nodeId").GetString()!, ncrId, "identified-in-ncr"));

        await PostDataAsync(browser,
            $"/api/business-console/v1/mes/production-reports/{reportNo}/reverse" +
            $"?organizationId={OrganizationId}&environmentId={EnvironmentId}",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                reportNo,
                reason = "MAN-2813 exact convergence",
                reversedAtUtc = DateTimeOffset.UtcNow,
                idempotencyKey = "man2813-rework-report-reverse",
            });

        await Eventually.WaitAsync(
            condition: "reversal converges ERP rework labor cost to exact zero and removes only active output lineage",
            observe: async token => new
            {
                Cost = await GetDataAsync(erp, HttpMethod.Get,
                    $"/api/business/v1/erp/finance/work-order-costs?organizationId={OrganizationId}&environmentId={EnvironmentId}&workOrderId={reworkWorkOrderId}", token),
                Trace = await GetDataAsync(browser, HttpMethod.Get,
                    $"/api/business-console/v1/mes/traceability/work-orders/{reworkWorkOrderId}?organizationId={OrganizationId}&environmentId={EnvironmentId}", token),
            },
            isSatisfied: state =>
                state.Cost.GetProperty("total").GetInt32() == 1 &&
                state.Cost.GetProperty("reworkCostTotal").GetDecimal() == 0m &&
                !HasEdge(state.Trace, reportNo!, "LOT-MAN2813-REWORK-OUTPUT", "produced-lot") &&
                HasEdge(state.Trace, ncrId, reworkWorkOrderId!, "created-rework-work-order"),
            describe: state => JsonSerializer.Serialize(new
            {
                reworkCost = state.Cost.GetProperty("reworkCostTotal").GetDecimal(),
                traceEdges = state.Trace.GetProperty("edges").GetArrayLength(),
            }),
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(500),
                SensitiveValues: [endpoints.InternalToken]));
    }

    private static bool HasEdge(JsonElement trace, string fromNodeId, string toNodeId, string relationType) =>
        trace.GetProperty("edges").EnumerateArray().Any(edge =>
            edge.GetProperty("fromNodeId").GetString() == fromNodeId &&
            edge.GetProperty("toNodeId").GetString() == toNodeId &&
            edge.GetProperty("relationType").GetString() == relationType);

    private static WebApplicationFactory<GatewayProgram> CreateGateway(ScenarioEndpoints endpoints) =>
        new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.UseSetting("Iam:Jwt:JwksJson", PublicGatewayToken.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", PublicGatewayToken.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", PublicGatewayToken.Audience);
            builder.UseSetting("MasterData:BaseUrl", endpoints.MasterData.ToString());
            builder.UseSetting("ProductEngineering:BaseUrl", endpoints.ProductEngineering.ToString());
            builder.UseSetting("Inventory:BaseUrl", endpoints.Inventory.ToString());
            builder.UseSetting("Approval:BaseUrl", endpoints.Approval.ToString());
            builder.UseSetting("Quality:BaseUrl", endpoints.Quality.ToString());
            builder.UseSetting("Mes:BaseUrl", endpoints.Mes.ToString());
            builder.UseSetting("Erp:BaseUrl", endpoints.Erp.ToString());
            builder.UseSetting("InternalService:BearerToken", endpoints.InternalToken);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(new AllowedAuthorizationClient());
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton(MasterDataProxy.Create());
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new StaticInternalServiceTokenProvider(endpoints.InternalToken));
            });
        });

    private static HttpClient InternalClient(Uri baseAddress, string token)
    {
        var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", $"user:{PrincipalId}");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-man2813-full-chain");
        client.DefaultRequestHeaders.Add("X-Causation-Id", "issue-2813");
        return client;
    }

    private static async Task<JsonElement> PostDataAsync(HttpClient client, string path, object body,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync(path, body, cancellationToken);
        return await DataAsync(response, cancellationToken);
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string path,
        CancellationToken cancellationToken = default) =>
        await GetDataAsync(client, HttpMethod.Get, path, cancellationToken);

    private static async Task<JsonElement> GetDataAsync(HttpClient client, HttpMethod method, string path,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await client.SendAsync(request, cancellationToken);
        return await DataAsync(response, cancellationToken);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.IsSuccessStatusCode,
            $"Expected HTTP success, got {(int)response.StatusCode} {response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<PostObservation> TryPostDataAsync(
        HttpClient client,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(path, body, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new(false, default, $"HTTP {(int)response.StatusCode}: {content}");
            }

            using var document = JsonDocument.Parse(content);
            return new(true, document.RootElement.GetProperty("data").Clone(), "success");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new(false, default, exception.Message);
        }
    }

    private static string StrongId(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : element.GetProperty("id").GetString()!;

    private sealed record PostObservation(bool IsSuccess, JsonElement Data, string Diagnostic);

    private sealed record ScenarioEndpoints(
        Uri MasterData,
        Uri ProductEngineering,
        Uri Inventory,
        Uri Approval,
        Uri Quality,
        Uri Mes,
        Uri Erp,
        string InternalToken)
    {
        public static ScenarioEndpoints FromEnvironment() => new(
            RequiredUri("NERV_IIP_TEST_MASTER_DATA_URL"),
            RequiredUri("NERV_IIP_TEST_PRODUCT_ENGINEERING_URL"),
            RequiredUri("NERV_IIP_TEST_INVENTORY_URL"),
            RequiredUri("NERV_IIP_TEST_APPROVAL_URL"),
            RequiredUri("NERV_IIP_TEST_QUALITY_URL"),
            RequiredUri("NERV_IIP_TEST_MES_URL"),
            RequiredUri("NERV_IIP_TEST_ERP_URL"),
            Required("NERV_IIP_TEST_INTERNAL_TOKEN"));

        private static Uri RequiredUri(string name) => new(Required(name), UriKind.Absolute);

        private static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{name} is required for MAN-2813 FullChain acceptance.");
    }

    private sealed class StaticInternalServiceTokenProvider(string token) : IInternalServiceTokenProvider
    {
        public string BearerToken { get; } = token;
    }

    private sealed class AllowedAuthorizationClient : IBusinessGatewayAuthorizationClient
    {
        public Task<BusinessGatewayAuthorizationResult> CheckAsync(
            string bearerToken,
            BusinessGatewayPermissionRequirement requirement,
            CancellationToken cancellationToken)
        {
            Assert.False(string.IsNullOrWhiteSpace(bearerToken));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BusinessGatewayAuthorizationResult.Allowed(
                PrincipalId,
                "user",
                "admin",
                requirement.OrganizationId,
                requirement.EnvironmentId,
                scopeGrants:
                [
                    new AuthorizationScopeGrant(
                        "role",
                        "man2813-acceptance",
                        "organization",
                        requirement.OrganizationId,
                        [requirement.PermissionCode],
                        OrganizationWide: true),
                ]));
        }
    }

    private class MasterDataProxy : DispatchProxy
    {
        public static IBusinessMasterDataClient Create() =>
            DispatchProxy.Create<IBusinessMasterDataClient, MasterDataProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name != nameof(IBusinessMasterDataClient.GetPrincipalWorkContextAsync))
            {
                throw new NotSupportedException($"MAN-2813 Gateway did not expect MasterData call '{targetMethod.Name}'.");
            }

            var request = Assert.IsType<BusinessMasterDataPrincipalWorkContextRequest>(args![1]);
            Assert.Equal(PrincipalId, request.UserId);
            var response = new BusinessMasterDataPrincipalWorkContextResponse(
                "resolved",
                null,
                [],
                [],
                [],
                [],
                [],
                [new BusinessMasterDataWorkContextCandidateScope(
                    "organization",
                    request.OrganizationId,
                    "MAN-2813 organization",
                    "organization",
                    [])],
                ["organization"],
                []);
            return Task.FromResult(response);
        }
    }

    private static class PublicGatewayToken
    {
        private const string Kid = "man2813-full-chain-key";
        private static readonly RSA Rsa = RSA.Create(2048);

        public const string Issuer = "nerv-iip-man2813-full-chain";
        public const string Audience = "nerv-iip-man2813-business-gateway";

        public static string ValidAccessToken(string organizationId, string environmentId)
        {
            var now = DateTimeOffset.UtcNow;
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, PrincipalId),
                    new Claim("sessionId", "man2813-full-chain-session"),
                    new Claim("principalType", "user"),
                    new Claim("loginName", "admin"),
                    new Claim("securityStamp", "man2813-full-chain-stamp"),
                    new Claim("permissionVersion", "1"),
                    new Claim("organizationId", organizationId),
                    new Claim("environmentId", environmentId),
                ],
                notBefore: now.AddMinutes(-1).UtcDateTime,
                expires: now.AddMinutes(30).UtcDateTime,
                signingCredentials: new SigningCredentials(
                    new RsaSecurityKey(Rsa) { KeyId = Kid },
                    SecurityAlgorithms.RsaSha256));
            token.Header["kid"] = Kid;
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static string PublicJwksJson()
        {
            var parameters = Rsa.ExportParameters(false);
            return $$"""
                {"keys":[{"kty":"RSA","use":"sig","kid":"{{Kid}}","alg":"RS256","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
                """;
        }
    }
}

internal sealed class RealNcrReworkFullChainFactAttribute : FactAttribute
{
    private static readonly string[] RequiredEnvironmentVariables =
    [
        "NERV_IIP_TEST_POSTGRES",
        "NERV_IIP_TEST_REDIS",
        "NERV_IIP_TEST_MASTER_DATA_URL",
        "NERV_IIP_TEST_PRODUCT_ENGINEERING_URL",
        "NERV_IIP_TEST_INVENTORY_URL",
        "NERV_IIP_TEST_APPROVAL_URL",
        "NERV_IIP_TEST_QUALITY_URL",
        "NERV_IIP_TEST_MES_URL",
        "NERV_IIP_TEST_ERP_URL",
        "NERV_IIP_TEST_INTERNAL_TOKEN",
    ];

    public RealNcrReworkFullChainFactAttribute()
    {
        var missing = RequiredEnvironmentVariables
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();
        if (missing.Length > 0)
        {
            Skip = "Set the MAN-2813 PostgreSQL, Redis, seven service URLs, and internal token variables to run the public NCR rework cost closure probe.";
        }
    }
}
