using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WmsDeadLetterReplayRegistrationTests
{
    [Fact]
    public async Task Every_active_consumer_is_constructible_and_has_one_original_replay_route()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventDeadLetterReplayHandler>().ToArray();
        var consumers = typeof(Program).Assembly.GetTypes().Where(t => t.GetCustomAttributesData()
            .Any(a => a.AttributeType == typeof(IntegrationEventConsumerAttribute))).ToArray();
        Assert.NotEmpty(consumers);
        foreach (var consumer in consumers)
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(consumer));
            var attribute = consumer.GetCustomAttributesData().Single(a => a.AttributeType == typeof(IntegrationEventConsumerAttribute));
            var name = Assert.IsType<string>(attribute.ConstructorArguments[1].Value);
            var contract = consumer.GetInterfaces().Single(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>)).GetGenericArguments()[0];
            var message = new IntegrationEventDeadLetterMessage(Guid.CreateVersion7(), name, "event", "event-type", 1,
                "source", "key", contract.FullName!, "{}", "original", "original failure", IntegrationEventDeadLetterStatus.Pending,
                DateTimeOffset.Parse("2026-10-01T00:00:00Z"), null);
            Assert.Single(handlers, h => h.CanReplay(message));
        }
        Assert.Equal(consumers.Length, handlers.Length);
    }
}
