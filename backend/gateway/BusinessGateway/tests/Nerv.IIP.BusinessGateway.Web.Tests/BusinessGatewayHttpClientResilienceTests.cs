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

    /// <summary>
    /// #3272：熔断打开后 <c>Polly.CircuitBreaker.BrokenCircuitException</c> 必须落到基类的
    /// 传输故障映射段，而不是逃逸成 500/「未知错误」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条用例刻意**不直接构造** <c>BrokenCircuitException</c>：那只能证明映射函数会映射，
    /// 证明不了这条路径走得到。它用生产的 <see cref="BusinessGatewayHttpClientResilience"/>
    /// 管道（FailureRatio 0.5 / MinimumThroughput 10 / SamplingDuration 30s）把下游打到
    /// 连续 10 次失败，让熔断**自己**打开，第 11 次调用才是被断言的那次。
    /// </para>
    /// <para>
    /// 判别力锚在三点，缺一不可：
    /// <list type="number">
    /// <item>第 11 次的 message 是 <c>downstream-circuit-open</c>；</item>
    /// <item>状态码是 503；</item>
    /// <item>第 11 次**没有打到下游**（<c>calls.Total</c> 仍是 10）——这一条才排除
    /// 「请求其实发出去了、只是又失败一次」。</item>
    /// </list>
    /// 另外断言第 1 次（熔断未开）的 message **不是**这条码：503 这个状态码熔断前后都出现，
    /// 只断言状态码或异常类型无法把两者分开。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Open_circuit_surfaces_downstream_circuit_open_instead_of_escaping_unmapped()
    {
        var calls = new DownstreamCallCounter();
        await using var factory = BusinessGatewayTestHost.CreateDedicatedFactory(
            configureBuilder: builder =>
            {
                builder.UseSetting("BarcodeLabel:BaseUrl", "http://barcode-label.local");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                        new BarcodeResolverUnavailableHandlerFilter(calls)));
            });

        var client = factory.Services.GetRequiredService<IBusinessBarcodeResolverClient>();
        var request = new BusinessBarcodeResolveRequest("org-001", "env-dev", "SN-0001", 0, 20);

        async Task<BusinessServiceProxyException> ResolveAsync() =>
            await Assert.ThrowsAsync<BusinessServiceProxyException>(
                () => client.ResolveAsync("internal-token", request, CancellationToken.None));

        // 熔断未开时的第一次：也失败、也是 503，但**不是**这条码。
        var beforeOpen = await ResolveAsync();
        Assert.NotEqual("downstream-circuit-open", beforeOpen.Message);

        // 打满 MinimumThroughput=10 次失败，熔断在第 10 次失败上打开。
        for (var attempt = 2; attempt <= MinimumThroughputToOpenCircuit; attempt++)
        {
            _ = await ResolveAsync();
        }

        Assert.Equal(MinimumThroughputToOpenCircuit, calls.Total);

        var afterOpen = await ResolveAsync();

        Assert.Equal("downstream-circuit-open", afterOpen.Message);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, afterOpen.StatusCode);
        // 熔断打开时请求根本没发往下游——这是本票文案敢说「本次请求未发出」的依据。
        Assert.Equal(MinimumThroughputToOpenCircuit, calls.Total);
    }

    /// <summary>
    /// 与 <see cref="BusinessGatewayHttpClientResilience"/> 里 <c>MinimumThroughput</c> 同值。
    /// 这里写成常量只是为了让上面的循环和断言读得出它的来源，不构成对生产值的约束。
    /// </summary>
    private const int MinimumThroughputToOpenCircuit = 10;

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

    private sealed class BarcodeResolverUnavailableHandlerFilter(DownstreamCallCounter calls)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                if (builder.Name == nameof(IBusinessBarcodeResolverClient))
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
