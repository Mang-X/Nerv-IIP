using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayProxyTests
{
    [Fact]
    public async Task List_skus_uses_internal_service_token_for_downstream_business_service()
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev&take=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 25), masterData.LastListResourcesRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SKU-001", document.RootElement.GetProperty("data").GetProperty("resources")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_skus_does_not_call_downstream_when_iam_denies_permission()
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Forbidden(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, masterData.ListResourcesCallCount);
    }

    [Fact]
    public async Task Inventory_availability_uses_internal_service_token_for_downstream_business_service()
    {
        var inventory = new RecordingInventoryClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/inventory/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", inventory.LastInternalToken);
        Assert.Equal("SKU-001", inventory.LastAvailabilityRequest!.SkuCode);
        Assert.Equal("S1", inventory.LastAvailabilityRequest.SiteCode);
    }

    [Fact]
    public async Task Quality_ncr_list_uses_internal_service_token_for_downstream_business_service()
    {
        var quality = new RecordingQualityClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(quality);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev&status=open&take=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", quality.LastInternalToken);
        Assert.Equal(new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", 20), quality.LastNcrListRequest);
    }

    [Fact]
    public async Task Mes_work_order_list_uses_internal_service_token_for_downstream_business_service()
    {
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&status=released&take=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", mes.LastInternalToken);
        Assert.Equal(new BusinessConsoleMesListRequest("org-001", "env-dev", "released", 15), mes.LastWorkOrderListRequest);
    }

    [Theory]
    [InlineData("/api/business-console/v1/inventory/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1", "inventory")]
    [InlineData("/api/business-console/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev", "quality")]
    [InlineData("/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev", "mes")]
    public async Task New_domain_facade_endpoints_do_not_call_downstream_when_iam_denies_permission(
        string path,
        string domain)
    {
        var inventory = new RecordingInventoryClient();
        var quality = new RecordingQualityClient();
        var mes = new RecordingMesClient();
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Forbidden(), services =>
        {
            services.RemoveAll<IBusinessInventoryClient>();
            services.AddSingleton<IBusinessInventoryClient>(inventory);
            services.RemoveAll<IBusinessQualityClient>();
            services.AddSingleton<IBusinessQualityClient>(quality);
            services.RemoveAll<IBusinessMesClient>();
            services.AddSingleton<IBusinessMesClient>(mes);
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(domain, new[] { "inventory", "quality", "mes" });
        Assert.Equal(0, inventory.AvailabilityCallCount);
        Assert.Equal(0, quality.NcrListCallCount);
        Assert.Equal(0, mes.WorkOrderListCallCount);
    }

    [Fact]
    public async Task List_skus_maps_downstream_service_error_to_gateway_error_response()
    {
        var masterData = new RecordingMasterDataClient
        {
            Failure = BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "master-data-unavailable"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("master-data-unavailable", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task List_skus_does_not_leak_raw_downstream_error_body_to_gateway_response()
    {
        var masterData = new RecordingMasterDataClient
        {
            Failure = new BusinessServiceProxyException(HttpStatusCode.BadGateway, "<html>secret stack trace</html>"),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("downstream-request-failed", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("secret stack trace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Master_data_http_client_sends_internal_bearer_token_and_builds_downstream_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resources = new[]
                {
                    new { resourceType = "sku", code = "SKU-HTTP", displayName = "HTTP SKU", active = true, snapshotVersion = "v1" },
                },
                total = 1,
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        var response = await client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", true, 12),
            CancellationToken.None);

        Assert.Equal("SKU-HTTP", response.Resources.Single().Code);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=sku&includeDisabled=true&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("internal-token-001", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Inventory_http_client_sends_internal_bearer_token_and_builds_downstream_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                skuCode = "SKU-HTTP",
                uomCode = "EA",
                siteCode = "S1",
                locationCode = (string?)null,
                lotNo = (string?)null,
                serialNo = (string?)null,
                qualityStatus = "available",
                ownerType = "owned",
                ownerId = (string?)null,
                onHandQuantity = 10,
                reservedQuantity = 2,
                availableQuantity = 8,
                items = Array.Empty<object>(),
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://inventory.local") };
        var client = new HttpBusinessInventoryClient(httpClient);

        var response = await client.GetAvailabilityAsync(
            "internal-token-001",
            new BusinessConsoleInventoryAvailabilityRequest("org-001", "env-dev", "SKU-HTTP", "EA", "S1", null, null, null, "available", "owned", null),
            CancellationToken.None);

        Assert.Equal(8, response.AvailableQuantity);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/inventory/v1/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-HTTP&uomCode=EA&siteCode=S1&qualityStatus=available&ownerType=owned", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Quality_http_client_sends_internal_bearer_token_and_builds_downstream_query()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        ncrId = "ncr-001",
                        ncrCode = "NCR-001",
                        sourceType = "inspection",
                        sourceDocumentId = "IR-001",
                        skuCode = "SKU-001",
                        defectQuantity = 1,
                        defectReason = "Defect",
                        status = "open",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.ListNcrsAsync(
            "internal-token-001",
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", 12),
            CancellationToken.None);

        Assert.Equal("ncr-001", response.Items.Single().Id);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev&status=open&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Quality_http_client_maps_real_downstream_inspection_plan_payload_to_console_items()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        inspectionPlanId = "plan-001",
                        planCode = "IP-001",
                        category = "incoming",
                        skuCode = "SKU-001",
                        status = "active",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.ListInspectionPlansAsync(
            "internal-token-001",
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "active", 12),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("plan-001", item.Id);
        Assert.Equal("IP-001", item.Code);
        Assert.Equal("active", item.Status);
        Assert.Contains("incoming", item.Summary, StringComparison.Ordinal);
        Assert.Contains("SKU-001", item.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quality_http_client_maps_real_downstream_ncr_payload_to_console_items()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        ncrId = "ncr-001",
                        ncrCode = "NCR-001",
                        sourceType = "inspection",
                        sourceDocumentId = "IR-001",
                        skuCode = "SKU-001",
                        defectQuantity = 3,
                        defectReason = "dimension-out-of-spec",
                        status = "open",
                    },
                },
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.ListNcrsAsync(
            "internal-token-001",
            new BusinessConsoleQualityListRequest("org-001", "env-dev", "open", 12),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("ncr-001", item.Id);
        Assert.Equal("NCR-001", item.Code);
        Assert.Equal("open", item.Status);
        Assert.Contains("inspection", item.Summary, StringComparison.Ordinal);
        Assert.Contains("SKU-001", item.Summary, StringComparison.Ordinal);
        Assert.Contains("dimension-out-of-spec", item.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quality_http_client_maps_inspection_record_to_real_downstream_request_shape()
    {
        string? requestBody = null;
        var handler = new RecordingHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, new
            {
                data = new
                {
                    inspectionRecordId = "inspection-001",
                },
                success = true,
                message = string.Empty,
                code = 0,
            });
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://quality.local") };
        var client = new HttpBusinessQualityClient(httpClient);

        var response = await client.CreateInspectionRecordAsync(
            "internal-token-001",
            new BusinessConsoleCreateInspectionRecordRequest(
                "org-001",
                "env-dev",
                "plan-001",
                "operation",
                "mes-operation",
                "OP-001",
                "SKU-001",
                10m,
                "LOT-001",
                null,
                [
                    new BusinessConsoleInspectionCharacteristicResult(
                        "dimension",
                        "10.2",
                        "mm",
                        "conditional-release",
                        "within-waiver-limit",
                        1m,
                        ["file-001"]),
                ],
                "waiver approved",
                ["disp-file-001"]),
            CancellationToken.None);

        Assert.Equal("inspection-001", response.InspectionRecordId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/business/v1/quality/inspection-records", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        Assert.Equal("operation", root.GetProperty("sourceType").GetString());
        Assert.Equal("mes-operation", root.GetProperty("sourceService").GetString());
        var line = root.GetProperty("resultLines")[0];
        Assert.Equal("dimension", line.GetProperty("characteristicCode").GetString());
        Assert.Equal("10.2", line.GetProperty("observedValue").GetString());
        Assert.Equal("mm", line.GetProperty("unitCode").GetString());
        Assert.Equal("conditional-release", line.GetProperty("result").GetString());
        Assert.Equal("within-waiver-limit", line.GetProperty("defectReason").GetString());
        Assert.Equal(1m, line.GetProperty("defectQuantity").GetDecimal());
        Assert.False(line.TryGetProperty("measuredValue", out _));
        Assert.False(line.TryGetProperty("dispositionReason", out _));
        Assert.False(line.TryGetProperty("defectCode", out _));
    }

    [Fact]
    public async Task Mes_http_client_sends_internal_bearer_token_and_builds_downstream_body()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            items = new[]
            {
                new
                {
                    workOrderId = "WO-HTTP",
                    skuId = "SKU-001",
                    productionVersionId = (string?)null,
                    quantity = 10,
                    priority = 0,
                    dueUtc = DateTimeOffset.Parse("2026-05-24T00:00:00Z"),
                    status = "released",
                    operationTasks = Array.Empty<object>(),
                },
            },
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.local") };
        var client = new HttpBusinessMesClient(httpClient);

        var response = await client.ListWorkOrdersAsync(
            "internal-token-001",
            new BusinessConsoleMesListRequest("org-001", "env-dev", "released", 12),
            CancellationToken.None);

        Assert.Equal("WO-HTTP", response.Items.Single().WorkOrderId);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev&status=released&take=12", request.RequestUri!.PathAndQuery);
        Assert.Equal("internal-token-001", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Master_data_http_client_forwards_accept_language_through_gateway_handler()
    {
        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        };
        contextAccessor.HttpContext.Request.Headers.AcceptLanguage = "zh-CN, en;q=0.8";
        var terminal = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                resources = Array.Empty<object>(),
                total = 0,
            },
            success = true,
            message = string.Empty,
            code = 0,
        }));
        using var httpClient = new HttpClient(new AcceptLanguageForwardingHandler(contextAccessor)
        {
            InnerHandler = terminal,
        })
        {
            BaseAddress = new Uri("http://master-data.local"),
        };
        var client = new HttpBusinessMasterDataClient(httpClient);

        await client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 100),
            CancellationToken.None);

        Assert.Equal(
            "zh-CN, en; q=0.8",
            string.Join(", ", terminal.Requests.Single().Headers.AcceptLanguage.Select(value => value.ToString())));
    }

    [Fact]
    public async Task Master_data_http_client_throws_proxy_exception_for_downstream_errors()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.BadRequest, new
        {
            success = false,
            message = "invalid-resource-type",
            code = 400,
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 100),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("invalid-resource-type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_data_http_client_does_not_expose_plain_text_downstream_error_bodies()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>secret stack trace</html>"),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.local") };
        var client = new HttpBusinessMasterDataClient(httpClient);

        var ex = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => client.ListResourcesAsync(
            "internal-token-001",
            new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 100),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal("downstream-request-failed", ex.Message);
        Assert.DoesNotContain("secret stack trace", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html>", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        Action<IServiceCollection>? configureServices = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:SigningKey", BusinessGatewayTestTokens.SigningKey);
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                configureServices?.Invoke(services);
            });
        });

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object payload) => new(statusCode)
    {
        Content = JsonContent.Create(payload),
    };

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}

internal sealed class RecordingMasterDataClient : IBusinessMasterDataClient
{
    public int ListResourcesCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleListResourcesRequest? LastListResourcesRequest { get; private set; }

    public BusinessServiceProxyException? Failure { get; init; }

    public Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken)
    {
        ListResourcesCallCount++;
        LastInternalToken = internalBearerToken;
        LastListResourcesRequest = request;
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(new BusinessConsoleResourceListResponse(
            [
                new BusinessConsoleResourceItem("sku", "SKU-001", "Demo SKU", true, "v1"),
            ],
            1));
    }

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        return Task.FromResult(new BusinessConsoleResourceItem("sku", request.Code, request.Name, true, "v1"));
    }
}

internal sealed class RecordingInventoryClient : IBusinessInventoryClient
{
    public int AvailabilityCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleInventoryAvailabilityRequest? LastAvailabilityRequest { get; private set; }

    public Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleInventoryAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        AvailabilityCallCount++;
        LastInternalToken = internalBearerToken;
        LastAvailabilityRequest = request;
        return Task.FromResult(new BusinessConsoleInventoryAvailabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            10,
            2,
            8,
            []));
    }

    public Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(
        string internalBearerToken,
        BusinessConsolePostStockMovementRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsolePostStockMovementResponse("move-001", 10, 8));

    public Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(
        string internalBearerToken,
        BusinessConsoleCreateStockCountTaskRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleCreateStockCountTaskResponse("count-001", 1));

    public Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(
        string internalBearerToken,
        string countTaskId,
        BusinessConsoleConfirmStockCountAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleConfirmStockCountAdjustmentResponse("move-001", 1, 11));
}

internal sealed class RecordingQualityClient : IBusinessQualityClient
{
    public int NcrListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleQualityListRequest? LastNcrListRequest { get; private set; }

    public Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleQualityListResponse([]));

    public Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(
        string internalBearerToken,
        BusinessConsoleCreateInspectionRecordRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleCreateInspectionRecordResponse("inspection-001"));

    public Task<BusinessConsoleQualityListResponse> ListNcrsAsync(
        string internalBearerToken,
        BusinessConsoleQualityListRequest request,
        CancellationToken cancellationToken)
    {
        NcrListCallCount++;
        LastInternalToken = internalBearerToken;
        LastNcrListRequest = request;
        return Task.FromResult(new BusinessConsoleQualityListResponse(
            [new BusinessConsoleQualityItem("ncr-001", "NCR-001", "open", "Defect")]));
    }

    public Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrDispositionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleAcceptedResponse(true));

    public Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(
        string internalBearerToken,
        string ncrId,
        BusinessConsoleNcrCloseRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleAcceptedResponse(true));
}

internal sealed class RecordingMesClient : IBusinessMesClient
{
    public int WorkOrderListCallCount { get; private set; }

    public string? LastInternalToken { get; private set; }

    public BusinessConsoleMesListRequest? LastWorkOrderListRequest { get; private set; }

    public Task<BusinessConsoleMesWorkOrderListResponse> ListWorkOrdersAsync(
        string internalBearerToken,
        BusinessConsoleMesListRequest request,
        CancellationToken cancellationToken)
    {
        WorkOrderListCallCount++;
        LastInternalToken = internalBearerToken;
        LastWorkOrderListRequest = request;
        return Task.FromResult(new BusinessConsoleMesWorkOrderListResponse(
            [
                new BusinessConsoleMesWorkOrderItem(
                    "wo-001",
                    "SKU-001",
                    null,
                    10,
                    0,
                    DateTimeOffset.Parse("2026-05-24T00:00:00Z"),
                    "released",
                    []),
            ]));
    }

    public Task<BusinessConsoleCreateRushWorkOrderResponse> CreateRushWorkOrderAsync(
        string internalBearerToken,
        BusinessConsoleCreateRushWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleMesScheduleResult> RunScheduleAsync(
        string internalBearerToken,
        BusinessConsoleRunScheduleRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(
        string internalBearerToken,
        BusinessConsoleRecordProductionReportRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
