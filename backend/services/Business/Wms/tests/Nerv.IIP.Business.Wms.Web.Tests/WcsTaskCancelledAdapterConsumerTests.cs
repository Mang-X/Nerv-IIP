using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Infrastructure;
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
        await using var dbContext = CreateDbContext();
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
            dbContext,
            adapter,
            deadLetters,
            new TestLogger<WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

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
    public async Task WcsTaskCancelledHandler_RecordsLocalInboxBeforeSendingAdapterCancellation()
    {
        await using var dbContext = CreateDbContext();
        var integrationEvent = BuildWcsTaskCancelledIntegrationEvent("WCS-CANCEL-IDEM-001");
        var adapter = new RecordingWcsCancellationAdapter();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation(
            dbContext,
            adapter,
            deadLetters,
            new TestLogger<WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var request = Assert.Single(adapter.Requests);
        Assert.Equal("WCS-CANCEL-IDEM-001", request.ExternalTaskId);
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task WcsTaskCancelledHandler_DeadLettersWhenAdapterEndpointIsNotConfigured()
    {
        await using var dbContext = CreateDbContext();
        var integrationEvent = BuildWcsTaskCancelledIntegrationEvent("WCS-CANCEL-NOCFG-001");
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler())
        {
            BaseAddress = new Uri("http://wcs.test"),
        };
        var adapter = new HttpWcsCancellationAdapter(
            httpClient,
            new ConfigurationBuilder().Build(),
            new TestLogger<HttpWcsCancellationAdapter>());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation(
            dbContext,
            adapter,
            deadLetters,
            new TestLogger<WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-wcs-adapter-endpoint", deadLetter.FailureCode);
    }

    [Fact]
    public async Task WcsTaskCancelledHandler_IgnoresSharedWmsTopicEventsForOtherTypes()
    {
        await using var dbContext = CreateDbContext();
        var adapter = new RecordingWcsCancellationAdapter();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation(
            dbContext,
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

        public bool CanHandle(WcsCancellationRequest request, out string? failureMessage)
        {
            failureMessage = null;
            return true;
        }

        public Task CancelAsync(WcsCancellationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private static WmsIntegrationEvent BuildWcsTaskCancelledIntegrationEvent(string externalTaskId)
    {
        var warehouseTask = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            $"TASK-{externalTaskId}",
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
            externalTaskId,
            """{"mission":"pick"}""");
        wcsTask.Fail("UPSTREAM_CANCELLED", "customer-requested-cancel");
        wcsTask.Cancel();
        return new WcsTaskCancelledIntegrationEventConverter()
            .Convert(new WcsTaskCancelledDomainEvent(wcsTask));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"wms-wcs-task-cancel-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot stream requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Noop mediator cannot stream requests.");
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
