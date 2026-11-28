using System.Net;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Server;

namespace Nerv.IIP.ConnectorHost.DevSimulator;

public sealed class MqttDevBrokerService(SimulatedPlantState plant, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointBoundIPV6Address(IPAddress.IPv6Loopback)
            .WithDefaultEndpointPort(configuration.GetValue("DevSimulator:MqttPort", 11883))
            .Build();
        using var broker = new MqttServerFactory().CreateMqttServer(options);
        await broker.StartAsync();

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var snapshot = plant.Current;
                await PublishAsync(broker, "DEV-ASM-01", new { pressForce = snapshot.PressForce, cycleCount = snapshot.CycleCount });
                await PublishAsync(broker, "DEV-TST-01", new { dampingForce = snapshot.DampingForce, oilTemperature = snapshot.OilTemperature });
            }
        }
        finally
        {
            await broker.StopAsync();
        }
    }

    private static Task PublishAsync(MqttServer broker, string device, object payload) =>
        broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
            .WithTopic($"factory/{device}/telemetry")
            .WithPayload(JsonSerializer.Serialize(payload))
            .Build())
        {
            SenderClientId = "dev-simulator"
        });
}
