using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nerv.IIP.BusinessGateway.Web;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayHttpClientResilienceTests
{
    [Fact]
    public async Task Non_idempotent_business_gateway_resilience_has_no_retry_strategy()
    {
        var calls = new DownstreamCallCounter();
        var services = new ServiceCollection();
        services
            .AddHttpClient("non-idempotent-safe")
            .ConfigurePrimaryHttpMessageHandler(() => new DownstreamUnavailableHandler(calls))
            .AddBusinessGatewayNonIdempotentSafeResilience();
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("non-idempotent-safe");

        var response = await client.GetAsync("http://downstream.local/unavailable");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, calls.Total);
    }

    [Fact]
    public async Task Business_service_clients_do_not_retry_server_errors()
    {
        var calls = new DownstreamCallCounter();
        await using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            configureBuilder: builder =>
            {
                builder.UseSetting("MasterData:BaseUrl", "http://master-data.local");
                builder.UseSetting("Inventory:BaseUrl", "http://inventory.local");
                builder.UseSetting("Quality:BaseUrl", "http://quality.local");
                builder.UseSetting("ProductEngineering:BaseUrl", "http://engineering.local");
                builder.UseSetting("DemandPlanning:BaseUrl", "http://planning.local");
                builder.UseSetting("Mes:BaseUrl", "http://mes.local");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                        new DownstreamUnavailableHandlerFilter(calls)));
            });

        var invocations = new Func<IServiceProvider, Task>[]
        {
            services => services.GetRequiredService<IBusinessMasterDataClient>().CreateSkuAsync(
                "internal-token",
                new BusinessConsoleCreateSkuRequest(
                    "org-001",
                    "env-dev",
                    "SKU-001",
                    "Finished Good",
                    "EA",
                    "finished",
                    "fg",
                    "batch",
                    "none",
                    "none",
                    "ambient",
                    "default",
                    true,
                    [],
                    "idem-masterdata-001"),
                CancellationToken.None),
            services => services.GetRequiredService<IBusinessInventoryClient>().PostMovementAsync(
                "internal-token",
                new BusinessConsolePostStockMovementRequest(
                    "org-001",
                    "env-dev",
                    "receipt",
                    "business-gateway-test",
                    "doc-001",
                    null,
                    "idem-inventory-001",
                    "SKU-001",
                    "EA",
                    "SITE-001",
                    "LOC-001",
                    null,
                    null,
                    "qualified",
                    "own",
                    null,
                    1),
                CancellationToken.None),
            services => services.GetRequiredService<IBusinessQualityClient>().CreateInspectionRecordAsync(
                "internal-token",
                new BusinessConsoleCreateInspectionRecordRequest(
                    "org-001",
                    "env-dev",
                    null,
                    "receipt",
                    "business-gateway-test",
                    "doc-001",
                    "SKU-001",
                    1,
                    null,
                    null,
                    [],
                    null,
                    []),
                CancellationToken.None),
            services => services.GetRequiredService<IBusinessProductEngineeringClient>().ResolveProductionVersionAsync(
                "internal-token",
                new BusinessConsoleResolveProductionVersionRequest(
                    "org-001",
                    "env-dev",
                    "SKU-001",
                    new DateOnly(2026, 5, 28),
                    1),
                CancellationToken.None),
            services => services.GetRequiredService<IBusinessPlanningClient>().RunMrpAsync(
                "internal-token",
                new BusinessConsoleRunMrpRequest(
                    "org-001",
                    "env-dev",
                    new DateOnly(2026, 5, 28),
                    new DateOnly(2026, 6, 28)),
                CancellationToken.None),
            services => services.GetRequiredService<IBusinessMesClient>().RunScheduleAsync(
                "internal-token",
                new BusinessConsoleRunScheduleRequest(
                    "org-001",
                    "env-dev",
                    "manual"),
                CancellationToken.None),
        };

        foreach (var invoke in invocations)
        {
            var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() => invoke(factory.Services));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        }

        Assert.Equal(invocations.Length, calls.Total);
    }

    [Fact]
    public async Task Quality_scrap_reason_read_client_uses_standard_resilience()
    {
        var calls = new DownstreamCallCounter();
        await using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            configureBuilder: builder =>
            {
                builder.UseSetting("Quality:BaseUrl", "http://quality.local");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                        new QualityScrapReasonTransientHandlerFilter(calls)));
            });

        var response = await factory.Services
            .GetRequiredService<IBusinessQualityScrapReasonCodeClient>()
            .ListScrapQualityReasonCodesAsync(
                "internal-token",
                new BusinessConsoleScrapQualityReasonCodeListRequest("org-001", "env-dev"),
                CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(3, calls.Total);
    }

    [Fact]
    public async Task Mes_material_prevalidation_read_uses_standard_resilience()
    {
        var calls = new DownstreamCallCounter();
        await using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            configureBuilder: builder =>
            {
                builder.UseSetting("Mes:BaseUrl", "http://mes.local");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                        new MesMaterialPrevalidationTransientHandlerFilter(calls)));
            });

        var response = await factory.Services
            .GetRequiredService<IBusinessMesMaterialPrevalidationClient>()
            .PrevalidateAsync(
                "internal-token",
                "corr-001",
                new MesMaterialScanPrevalidationRequest(
                    "org-001", "env-dev", "MIR-001", "WO-001", "OP-10"),
                CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal(3, calls.Total);
    }

    [Theory]
    [InlineData(nameof(IBusinessGatewayAuthorizationClient), false)]
    [InlineData(nameof(IBusinessMasterDataClient), true)]
    [InlineData(nameof(IBusinessInventoryClient), true)]
    [InlineData(nameof(IBusinessQualityClient), true)]
    [InlineData(nameof(IBusinessProductEngineeringClient), true)]
    [InlineData(nameof(IBusinessPlanningClient), true)]
    [InlineData(nameof(IBusinessMesClient), true)]
    public void DownstreamUnavailableHandlerFilter_only_stubs_business_service_clients(
        string clientName,
        bool expectedStubbed)
    {
        var calls = new DownstreamCallCounter();
        var builder = new TestHttpMessageHandlerBuilder
        {
            Name = clientName,
            PrimaryHandler = new HttpClientHandler()
        };

        new DownstreamUnavailableHandlerFilter(calls).Configure(_ => { })(builder);

        Assert.Equal(expectedStubbed, builder.PrimaryHandler is DownstreamUnavailableHandler);
    }

    private sealed class DownstreamCallCounter
    {
        private int callCount;

        public int Total => callCount;

        public void Increment() => Interlocked.Increment(ref callCount);

        public int IncrementAndGet() => Interlocked.Increment(ref callCount);
    }

    private sealed class DownstreamUnavailableHandlerFilter(DownstreamCallCounter calls)
        : IHttpMessageHandlerBuilderFilter
    {
        private static readonly HashSet<string> StubbedClientNames =
        [
            nameof(IBusinessMasterDataClient),
            nameof(IBusinessInventoryClient),
            nameof(IBusinessQualityClient),
            nameof(IBusinessProductEngineeringClient),
            nameof(IBusinessPlanningClient),
            nameof(IBusinessMesClient)
        ];

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                if (builder.Name is not null && StubbedClientNames.Contains(builder.Name))
                {
                    builder.PrimaryHandler = new DownstreamUnavailableHandler(calls);
                }
            };
    }

    private sealed class QualityScrapReasonTransientHandlerFilter(DownstreamCallCounter calls)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                if (builder.Name == nameof(IBusinessQualityScrapReasonCodeClient))
                {
                    builder.PrimaryHandler = new QualityScrapReasonTransientHandler(calls);
                }
            };
    }

    private sealed class QualityScrapReasonTransientHandler(DownstreamCallCounter calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var attempt = calls.IncrementAndGet();
            if (attempt < 3)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":{\"items\":[],\"total\":0}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class MesMaterialPrevalidationTransientHandlerFilter(DownstreamCallCounter calls)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                if (builder.Name == nameof(IBusinessMesMaterialPrevalidationClient))
                {
                    builder.PrimaryHandler = new MesMaterialPrevalidationTransientHandler(calls);
                }
            };
    }

    private sealed class MesMaterialPrevalidationTransientHandler(DownstreamCallCounter calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var attempt = calls.IncrementAndGet();
            if (attempt < 3)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":{\"decision\":\"accepted\",\"reasonCode\":\"material-scan-accepted\",\"materialIssueRequestId\":\"MIR-001\",\"workOrderId\":\"WO-001\",\"operationTaskId\":\"OP-10\",\"materialId\":\"MAT-001\",\"materialLotId\":\"LOT-001\",\"materialQualification\":\"primary\",\"evaluatedAtUtc\":\"2026-08-26T08:00:00Z\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class DownstreamUnavailableHandler(DownstreamCallCounter calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            calls.Increment();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class TestHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];
        public override IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
