using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Web.Application.WcsAdapters;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WcsTaskCancelledAdapterConsumerTests
{
    [Fact]
    public async Task WcsTaskCancelledHandler_SendsCancelCommandToMatchingAdapter()
    {
        var warehouseTask = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-CANCEL-001",
            "OUT-CANCEL-001",
            "LINE-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "PACK-01",
            4m);
        var wcsTask = WcsTask.Dispatch(
            "org-001",
            "env-dev",
            warehouseTask.Id,
            "agv",
            "WCS-CANCEL-001",
            """{"mission":"pick"}""");
        wcsTask.Fail("UPSTREAM_CANCELLED", "customer-requested-cancel");
        wcsTask.Cancel();
        var integrationEvent = new WcsTaskCancelledIntegrationEventConverter()
            .Convert(new WcsTaskCancelledDomainEvent(wcsTask));
        var adapter = new RecordingWcsCancellationAdapter();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation(
            adapter,
            deadLetters,
            new TestLogger<WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var request = Assert.Single(adapter.Requests);
        Assert.Equal("org-001", request.OrganizationId);
        Assert.Equal("env-dev", request.EnvironmentId);
        Assert.Equal("agv", request.AdapterType);
        Assert.Equal("WCS-CANCEL-001", request.ExternalTaskId);
        Assert.Equal("customer-requested-cancel", request.Reason);
        Assert.Equal(integrationEvent.IdempotencyKey, request.IdempotencyKey);
        Assert.Empty(await deadLetters.ListAsync(
            WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task WcsTaskCancelledHandler_IgnoresSharedWmsTopicEventsForOtherTypes()
    {
        var adapter = new RecordingWcsCancellationAdapter();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation(
            adapter,
            deadLetters,
            new TestLogger<WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation>());
        var integrationEvent = new WmsIntegrationEvent(
            "evt-test",
            WmsIntegrationEventTypes.WcsTaskCompleted,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            WmsIntegrationEventSources.BusinessWms,
            "corr-test",
            "cause-test",
            "org-001",
            "env-dev",
            "system:wms",
            "idem-test",
            new WmsIntegrationPayload("WCS-COMPLETE-001", null, null, null, null, null, null, "Completed", null, null));

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Empty(adapter.Requests);
        Assert.Empty(await deadLetters.ListAsync(
            WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task HttpWcsCancellationAdapter_PostsConfiguredCancelCommand()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://wcs.test"),
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wcs:Adapters:agv:CancelEndpoint"] = "/api/wcs/tasks/cancel",
            })
            .Build();
        var adapter = new HttpWcsCancellationAdapter(
            httpClient,
            configuration,
            new TestLogger<HttpWcsCancellationAdapter>());

        await adapter.CancelAsync(
            new WcsCancellationRequest(
                "org-001",
                "env-dev",
                "agv",
                "WCS-CANCEL-001",
                "customer-requested-cancel",
                "idem-wcs-cancel"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://wcs.test/api/wcs/tasks/cancel", handler.Request.RequestUri!.ToString());
        var body = await handler.Request.Content!.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("\"adapterType\":\"agv\"", body, StringComparison.Ordinal);
        Assert.Contains("\"externalTaskId\":\"WCS-CANCEL-001\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"customer-requested-cancel\"", body, StringComparison.Ordinal);
        Assert.Contains("\"idempotencyKey\":\"idem-wcs-cancel\"", body, StringComparison.Ordinal);
    }

    private sealed class RecordingWcsCancellationAdapter : IWcsCancellationAdapter
    {
        public List<WcsCancellationRequest> Requests { get; } = [];

        public Task CancelAsync(WcsCancellationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("""{"accepted":true}"""),
            });
        }
    }
}
