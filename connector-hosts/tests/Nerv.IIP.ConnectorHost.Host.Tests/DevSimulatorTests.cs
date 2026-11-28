using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using Nerv.IIP.ConnectorHost.DevSimulator;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

public sealed class DevSimulatorTests
{
    [Fact]
    public void Plant_faults_return_to_normal_after_thirty_seconds()
    {
        Assert.Equal(79f, ReadFloat(180, unit: 1));
        Assert.InRange(ReadFloat(210, unit: 1), 54f, 63f);
        Assert.Equal(5.4f, ReadFloat(240, unit: 2));
        Assert.InRange(ReadFloat(270, unit: 2), 6.8f, 7.6f);
        Assert.Equal(18.8, SimulatedPlantState.CreateSnapshot(210).PressForce);
        Assert.InRange(SimulatedPlantState.CreateSnapshot(240).PressForce, 12, 14.5);

        static float ReadFloat(int seconds, int unit)
        {
            var registers = SimulatedPlantState.CreateSnapshot(seconds).Registers[unit - 1];
            return BitConverter.UInt32BitsToSingle(((uint)registers[0] << 16) | registers[1]);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Modbus_serves_big_endian_float_and_uint16_registers(byte function)
    {
        var port = AvailablePort();
        using var service = new ModbusTcpSlaveService(new SimulatedPlantState(), Configure("ModbusPort", port));
        await service.StartAsync(CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = await ConnectWhenReadyAsync(port, deadline.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 0, 5, 0, 0, 0, 6, 1, function, 0, 0, 0, 7 }, deadline.Token);
            var result = new byte[23];
            await stream.ReadExactlyAsync(result, deadline.Token);
            Assert.Equal(new byte[] { 0, 5, 0, 0, 0, 17, 1, function, 14 }, result[..9]);
            Assert.InRange(BinaryPrimitives.ReadSingleBigEndian(result.AsSpan(9)), 54f, 63f);
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(21)));

            await stream.WriteAsync(new byte[] { 0, 6, 0, 0, 0, 6, 1, 6, 0, 6, 0, 15 }, deadline.Token);
            var exception = new byte[9];
            await stream.ReadExactlyAsync(exception, deadline.Token);
            Assert.Equal(new byte[] { 0, 6, 0, 0, 0, 3, 1, 134, 1 }, exception);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Mqtt_broker_publishes_both_devices_to_a_real_subscriber()
    {
        var port = AvailablePort();
        using var service = new MqttDevBrokerService(new SimulatedPlantState(), Configure("MqttPort", port));
        await service.StartAsync(CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (await ConnectWhenReadyAsync(port, deadline.Token)) { }
            var factory = new MqttClientFactory();
            using var client = factory.CreateMqttClient();
            var assembly = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var test = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.ApplicationMessageReceivedAsync += args =>
            {
                var signal = args.ApplicationMessage.Topic == "factory/DEV-ASM-01/telemetry" ? assembly : test;
                signal.TrySetResult(args.ApplicationMessage.ConvertPayloadToString());
                return Task.CompletedTask;
            };

            await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).Build(), deadline.Token);
            await client.SubscribeAsync(factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter("factory/+/telemetry")
                .Build(), deadline.Token);

            using var assemblyData = JsonDocument.Parse(await BoundedObservation.ObserveAsync(
                assembly.Task, "assembly telemetry", () => "awaiting MQTT publish"));
            using var testData = JsonDocument.Parse(await BoundedObservation.ObserveAsync(
                test.Task, "test rig telemetry", () => "awaiting MQTT publish"));
            Assert.InRange(assemblyData.RootElement.GetProperty("pressForce").GetDouble(), 12, 14.5);
            Assert.Equal(0, assemblyData.RootElement.GetProperty("cycleCount").GetInt32());
            Assert.InRange(testData.RootElement.GetProperty("dampingForce").GetDouble(), 2.1, 2.7);
            Assert.InRange(testData.RootElement.GetProperty("oilTemperature").GetDouble(), 34, 40);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static IConfiguration Configure(string name, int port) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"DevSimulator:{name}"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).Build();

    private static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<TcpClient> ConnectWhenReadyAsync(int port, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            using var retry = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            do
            {
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, port, ct);
                    return client;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                }
            } while (await retry.WaitForNextTickAsync(ct));

            throw new InvalidOperationException("Simulator listener did not start.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
