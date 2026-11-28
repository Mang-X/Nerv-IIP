using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Nerv.IIP.ConnectorHost.DevSimulator;

public sealed class ModbusTcpSlaveService(SimulatedPlantState plant, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, configuration.GetValue("DevSimulator:ModbusPort", 15020));
        listener.Start();
        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stoppingToken);
            _ = ServeAsync(client, stoppingToken);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            var header = new byte[7];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(header, cancellationToken);
                    var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                    if (header[2] != 0 || header[3] != 0 || length is < 2 or > 254)
                    {
                        return;
                    }

                    var request = new byte[length - 1];
                    await stream.ReadExactlyAsync(request, cancellationToken);
                    var response = ReadRegisters(header[6], request);
                    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(response.Length + 1));
                    await stream.WriteAsync(header, cancellationToken);
                    await stream.WriteAsync(response, cancellationToken);
                }
            }
            catch (IOException)
            {
                // The client disconnected.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private byte[] ReadRegisters(byte unit, byte[] request)
    {
        var function = request[0];
        if (function is not (3 or 4))
        {
            return [(byte)(function | 0x80), 1];
        }

        if (request.Length != 5)
        {
            return [(byte)(function | 0x80), 3];
        }

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(1));
        var count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(3));
        if (count is < 1 or > 125)
        {
            return [(byte)(function | 0x80), 3];
        }

        var registers = plant.Current.Registers;
        if (unit < 1 || unit > registers.Length || address + count > registers[unit - 1].Length)
        {
            return [(byte)(function | 0x80), 2];
        }

        var response = new byte[2 + count * 2];
        response[0] = function;
        response[1] = (byte)(count * 2);
        for (var index = 0; index < count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + index * 2), registers[unit - 1][address + index]);
        }

        return response;
    }
}
