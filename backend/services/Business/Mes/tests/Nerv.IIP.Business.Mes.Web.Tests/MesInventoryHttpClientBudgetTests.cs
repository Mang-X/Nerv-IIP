using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesInventoryHttpClientBudgetTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Default_connection_and_request_budgets_are_five_and_ten_seconds()
    {
        var filter = new InventoryHandlerFilter();
        await using var factory = CreateFactory(filter);

        var options = factory.Services.GetRequiredService<IOptions<MesInventoryHttpClientOptions>>().Value;
        var client = factory.Services.GetRequiredService<MesInventoryHttpClient>().HttpClient;
        var handler = Assert.IsType<SocketsHttpHandler>(filter.PrimaryHandler);

        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    [Fact]
    public async Task Configured_connection_budget_cancels_a_real_blocking_connect_callback()
    {
        var connect = new BlockingConnectCallback();
        var filter = new InventoryHandlerFilter(handler =>
        {
            var sockets = Assert.IsType<SocketsHttpHandler>(handler);
            sockets.ConnectCallback = connect.ConnectAsync;
            return sockets;
        });
        await using var factory = CreateFactory(
            filter,
            connectTimeout: TimeSpan.FromMilliseconds(50),
            requestTimeout: TimeSpan.FromSeconds(5));
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IMesMaterialLotAvailabilityProvider>();

        var exception = await Assert.ThrowsAsync<KnownException>(() => TestTimeout.RunAsync(
            "MES Inventory connect timeout contract",
            async token => await provider.GetAsync(AvailabilityRequest(), token),
            TestDeadline).AsTask());

        Assert.IsNotType<TestTimeoutException>(exception);
        await TestTimeout.RunAsync(
            "MES Inventory connect callback cancellation observation",
            async token => await connect.Canceled.Task.WaitAsync(token),
            TestDeadline);
    }

    [Fact]
    public async Task Configured_request_budget_cancels_a_real_blocking_http_handler()
    {
        var blocking = new BlockingHttpHandler();
        var filter = new InventoryHandlerFilter(_ => blocking);
        await using var factory = CreateFactory(
            filter,
            connectTimeout: TimeSpan.FromSeconds(1),
            requestTimeout: TimeSpan.FromMilliseconds(50));
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IMesMaterialLotAvailabilityProvider>();

        var exception = await Assert.ThrowsAsync<KnownException>(() => TestTimeout.RunAsync(
            "MES Inventory request timeout contract",
            async token => await provider.GetAsync(AvailabilityRequest(), token),
            TestDeadline).AsTask());

        Assert.IsNotType<TestTimeoutException>(exception);
        await TestTimeout.RunAsync(
            "MES Inventory request handler cancellation observation",
            async token => await blocking.Canceled.Task.WaitAsync(token),
            TestDeadline);
    }

    [Fact]
    public async Task Caller_cancellation_flows_through_the_DI_created_inventory_client()
    {
        var blocking = new BlockingHttpHandler();
        var filter = new InventoryHandlerFilter(_ => blocking);
        await using var factory = CreateFactory(
            filter,
            connectTimeout: TimeSpan.FromSeconds(1),
            requestTimeout: TimeSpan.FromSeconds(1));
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IMesMaterialLotAvailabilityProvider>();
        using var caller = new CancellationTokenSource();

        var request = provider.GetAsync(AvailabilityRequest(), caller.Token);
        await TestTimeout.RunAsync(
            "MES Inventory caller cancellation request start",
            async token => await blocking.Started.Task.WaitAsync(token),
            TestDeadline);
        caller.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestTimeout.RunAsync(
            "MES Inventory caller cancellation completion",
            async token => await request.WaitAsync(token),
            TestDeadline).AsTask());
        Assert.IsNotType<TestTimeoutException>(exception);
        Assert.True(blocking.CancellationToken.IsCancellationRequested);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        InventoryHandlerFilter filter,
        TimeSpan? connectTimeout = null,
        TimeSpan? requestTimeout = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                if (connectTimeout is not null)
                {
                    builder.UseSetting("Mes:InventoryClient:ConnectTimeout", connectTimeout.Value.ToString("c"));
                }

                if (requestTimeout is not null)
                {
                    builder.UseSetting("Mes:InventoryClient:RequestTimeout", requestTimeout.Value.ToString("c"));
                }

                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");

                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(filter));
            });

    private static MesMaterialLotAvailabilityRequest AvailabilityRequest() =>
        new("org-001", "env-dev", "MAT-001", "PCS", "SITE-01", "LINE-01", "LOT-001", new DateOnly(2026, 8, 28));

    private sealed class InventoryHandlerFilter(
        Func<HttpMessageHandler, HttpMessageHandler>? configure = null) : IHttpMessageHandlerBuilderFilter
    {
        public HttpMessageHandler? PrimaryHandler { get; private set; }

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name != nameof(MesInventoryHttpClient))
            {
                return;
            }

            builder.PrimaryHandler = configure?.Invoke(builder.PrimaryHandler) ?? builder.PrimaryHandler;
            PrimaryHandler = builder.PrimaryHandler;
        };
    }

    private sealed class BlockingConnectCallback
    {
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext _,
            CancellationToken cancellationToken)
        {
            try
            {
                await PendingOperation.UntilCanceledAsync(cancellationToken);
            }
            finally
            {
                Canceled.TrySetResult();
            }

            throw new InvalidOperationException("The blocking connect callback must be canceled.");
        }
    }

    private sealed class BlockingHttpHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CancellationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            CancellationToken = cancellationToken;
            Started.TrySetResult();
            try
            {
                await PendingOperation.UntilCanceledAsync(cancellationToken);
            }
            finally
            {
                Canceled.TrySetResult();
            }

            throw new InvalidOperationException("The blocking request handler must be canceled.");
        }
    }
}
