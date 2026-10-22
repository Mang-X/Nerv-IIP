using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class DemandPlanningDeadLetterReplayRegistrationTests
{
    // #4352: every existing consumer must be reachable through production composition.
    [Fact]
    public async Task Every_active_sales_demand_consumer_has_one_constructible_replay_route()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventDeadLetterReplayHandler>().ToArray();
        var consumers = typeof(Program).Assembly.GetTypes().Where(type => type.GetCustomAttributesData()
            .Any(attribute => attribute.AttributeType == typeof(IntegrationEventConsumerAttribute))).ToArray();
        Assert.Equal(4, consumers.Length);
        foreach (var consumer in consumers)
        {
            var attribute = consumer.GetCustomAttributesData().Single(x => x.AttributeType == typeof(IntegrationEventConsumerAttribute));
            var name = Assert.IsType<string>(attribute.ConstructorArguments[1].Value);
            var contract = consumer.GetInterfaces().Single(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>));
            var message = new IntegrationEventDeadLetterMessage(Guid.CreateVersion7(), name, "event", "event-type", 1,
                "source", "idempotency", contract.GetGenericArguments()[0].FullName!, "{}", "original", "original failure",
                IntegrationEventDeadLetterStatus.Pending, DateTimeOffset.Parse("2026-10-01T00:00:00Z"), null);
            Assert.Single(handlers, x => x.CanReplay(message));
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(consumer));
        }
        Assert.Equal(consumers.Length, handlers.Length);
    }
}
