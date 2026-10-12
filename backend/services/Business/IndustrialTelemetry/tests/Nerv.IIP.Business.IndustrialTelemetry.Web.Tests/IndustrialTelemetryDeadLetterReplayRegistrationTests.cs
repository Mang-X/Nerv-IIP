using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class IndustrialTelemetryDeadLetterReplayRegistrationTests
{
    [Fact]
    public async Task Every_active_industrial_telemetry_consumer_is_constructible_and_has_one_exact_replay_route()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => { builder.UseEnvironment("Testing"); builder.UseSetting("InternalService:BearerToken", "test-replay-token"); });
        using var scope = factory.Services.CreateScope();
        var replayHandlers = scope.ServiceProvider.GetServices<IIntegrationEventDeadLetterReplayHandler>().ToArray();
        var consumers = typeof(Program).Assembly.GetTypes().Where(type => type.GetCustomAttributesData()
            .Any(attribute => attribute.AttributeType == typeof(IntegrationEventConsumerAttribute))).ToArray();
        Assert.NotEmpty(consumers);
        foreach (var consumer in consumers)
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(consumer));
            var attribute = consumer.GetCustomAttributesData().Single(x => x.AttributeType == typeof(IntegrationEventConsumerAttribute));
            var name = Assert.IsType<string>(attribute.ConstructorArguments[1].Value);
            foreach (var contract in consumer.GetInterfaces().Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>)))
            {
                var eventType = contract.GetGenericArguments()[0];
                var message = new IntegrationEventDeadLetterMessage(Guid.CreateVersion7(), name, "event", "event-type", 1,
                    "source", "idempotency", eventType.FullName!, "{}", "original", "original failure", IntegrationEventDeadLetterStatus.Pending,
                    DateTimeOffset.Parse("2026-10-01T00:00:00Z"), null);
                Assert.Single(replayHandlers, x => x.CanReplay(message));
                Assert.DoesNotContain(replayHandlers, x => x.CanReplay(message with { ConsumerName = "undeclared" }));
            }
        }
        Assert.Equal(consumers.Length, replayHandlers.Length);
    }
}
