using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ErpDeliveryCausationHttpTests
{
    [Fact]
    public async Task Delivery_registration_without_causation_header_supplies_event_causation()
    {
        var captured = new List<ErpIntegrationEventContext>();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                ["InternalService:BearerToken"] = "test-delivery-token",
                ["Persistence:AutoMigrate"] = "false",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddScoped<ISender>(sp => new CapturingSender(
                    sp.GetRequiredService<IErpIntegrationEventContextAccessor>(), captured));
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-delivery-token");

        using var response = await client.PostAsJsonAsync("/api/business/v1/erp/delivery-orders", new
        {
            organizationId = "org-001", environmentId = "env-dev", deliveryOrderNo = "DO-001",
            salesOrderNo = "SO-001", lines = new[] { new { salesOrderLineNo = "10", quantity = 1m } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("command:release-delivery-order:", Assert.Single(captured).CausationId, StringComparison.Ordinal);
    }

    private sealed class CapturingSender(
        IErpIntegrationEventContextAccessor eventContext,
        List<ErpIntegrationEventContext> captured) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Assert.IsType<ReleaseDeliveryOrderCommand>(request);
            captured.Add(eventContext.GetContext());
            return Task.FromResult((TResponse)(object)new DeliveryOrderId(Guid.Parse("00000000-0000-0000-0000-000000003944")));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
