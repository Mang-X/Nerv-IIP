using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using MQTTnet;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application;

public sealed record TestTelemetryConnectorConnectionQuery(TelemetryConnectionTestInput Input) : IQuery<TelemetryConnectionTestResult>;

public sealed class TelemetryConnectionProbe : IQueryHandler<TestTelemetryConnectorConnectionQuery, TelemetryConnectionTestResult>
{
    public Task<TelemetryConnectionTestResult> Handle(TestTelemetryConnectorConnectionQuery request, CancellationToken cancellationToken) =>
        TestAsync(request.Input, cancellationToken);

    public async Task<TelemetryConnectionTestResult> TestAsync(TelemetryConnectionTestInput request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var protocol = request.Protocol.Trim().ToLowerInvariant();
            var endpoint = ParseEndpoint(protocol, request.Endpoint);
            decimal? sample = null;
            if (protocol == "mqtt")
            {
                using var client = new MqttClientFactory().CreateMqttClient();
                var result = await client.ConnectAsync(new MqttClientOptionsBuilder()
                    .WithTcpServer(endpoint.Host, endpoint.Port)
                    .Build(), deadline.Token);
                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    return new(false, started.ElapsedMilliseconds, "MQTT Broker 拒绝连接。", null);
                }
            }
            else
            {
                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, deadline.Token);
                if (protocol == "modbus")
                {
                    sample = await ReadModbusAsync(client.GetStream(), request.Mapping, deadline.Token);
                }
            }

            return new(true, started.ElapsedMilliseconds, "连接成功。", sample);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, started.ElapsedMilliseconds, "连接测试超时。", null);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OverflowException or MQTTnet.Exceptions.MqttCommunicationException or KnownException)
        {
            return new(false, started.ElapsedMilliseconds, "连接失败，请检查协议、地址和测点。", null);
        }
    }

    internal static (string Host, int Port) ParseEndpoint(string protocol, string endpoint)
    {
        var scheme = protocol == "opcua" ? "opc.tcp" : "tcp";
        if (protocol is not ("modbus" or "mqtt" or "opcua")
            || !Uri.TryCreate(endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : $"{scheme}://{endpoint}", UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Scheme != scheme && (protocol == "opcua" || uri.Scheme != protocol))
        {
            throw new KnownException("连接器协议或地址无效。");
        }

        var defaultPort = protocol switch { "modbus" => 502, "mqtt" => 1883, _ => 4840 };
        return (uri.Host, uri.Port > 0 ? uri.Port : defaultPort);
    }

    private static async Task<decimal> ReadModbusAsync(NetworkStream stream, TelemetryConnectorMapping? mapping, CancellationToken ct)
    {
        if (mapping is { Address: < 40001 or > 49999 })
        {
            throw new KnownException("Modbus 保持寄存器地址必须介于 40001 和 49999。");
        }

        var floating = mapping?.DataType == "Float32";
        var count = floating ? 2 : 1;
        byte[] frame = [0, 1, 0, 0, 0, 6, mapping?.UnitId ?? 1, 3, 0, 0, 0, (byte)count];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), checked((ushort)((mapping?.Address ?? 40001) - 40001)));
        await stream.WriteAsync(frame, ct);

        var header = new byte[7];
        await stream.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 4).SequenceEqual(frame.AsSpan(0, 4))
            || header[6] != frame[6] || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) != 3 + count * 2)
        {
            throw new IOException("Invalid Modbus response.");
        }

        var payload = new byte[2 + count * 2];
        await stream.ReadExactlyAsync(payload, ct);
        if (payload[0] != 3 || payload[1] != count * 2)
        {
            throw new IOException("Invalid Modbus register response.");
        }

        return floating
            ? (decimal)BinaryPrimitives.ReadSingleBigEndian(payload.AsSpan(2))
            : BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2));
    }
}
