using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Nerv.IIP.ConnectorHost.Connectors.Modbus;

namespace Nerv.IIP.ConnectorHost.Connectors.Modbus.Tests;

public sealed class ModbusTcpClientTests
{
    [Fact]
    public async Task Read_registers_validates_response_identity_and_decodes_float32_word_swap()
    {
        await using var simulator = await ModbusTcpSimulator.StartAsync(
            request =>
            {
                Assert.Equal(1, request.UnitId);
                Assert.Equal(0x03, request.FunctionCode);
                Assert.Equal(0, request.Address);
                Assert.Equal(2, request.Count);
                return new byte[] { 0x00, 0x00, 0x42, 0x2A };
            });
        using var client = new ModbusTcpClient();
        var mapping = new ModbusRegisterMapping(
            DeviceAssetId: "device-line-1",
            TagKey: "temperature",
            UnitId: 1,
            Table: ModbusRegisterTable.HoldingRegisters,
            Address: 40001,
            RegisterCount: 2,
            Scale: 1m,
            Offset: 0m,
            BucketSeconds: 60,
            DataType: ModbusRegisterDataType.Float32,
            WordOrder: ModbusWordOrder.LittleEndian);

        await client.ConnectAsync(new ModbusConnectionOptions(simulator.Endpoint, null), CancellationToken.None);
        var samples = await client.ReadRegistersAsync(mapping, new DateTimeOffset(2026, 7, 5, 8, 0, 10, TimeSpan.Zero), CancellationToken.None);

        var sample = Assert.Single(samples);
        Assert.Equal(42.5m, sample.Value);
    }

    [Fact]
    public async Task Read_registers_rejects_response_with_wrong_transaction_or_unit()
    {
        await using var simulator = await ModbusTcpSimulator.StartAsync(
            _ => [0x00, 0x2A],
            transactionIdOffset: 1);
        using var client = new ModbusTcpClient();
        var mapping = new ModbusRegisterMapping(
            DeviceAssetId: "device-line-1",
            TagKey: "temperature",
            UnitId: 1,
            Table: ModbusRegisterTable.HoldingRegisters,
            Address: 40001,
            RegisterCount: 1,
            Scale: 1m,
            Offset: 0m,
            BucketSeconds: 60,
            DataType: ModbusRegisterDataType.UInt16,
            WordOrder: ModbusWordOrder.BigEndian);

        await client.ConnectAsync(new ModbusConnectionOptions(simulator.Endpoint, null), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadRegistersAsync(mapping, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Contains("transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_registers_drops_non_finite_float32_without_throwing()
    {
        await using var simulator = await ModbusTcpSimulator.StartAsync(_ => [0x7F, 0xC0, 0x00, 0x00]);
        using var client = new ModbusTcpClient();
        var mapping = new ModbusRegisterMapping(
            DeviceAssetId: "device-line-1",
            TagKey: "temperature",
            UnitId: 1,
            Table: ModbusRegisterTable.HoldingRegisters,
            Address: 40001,
            RegisterCount: 2,
            Scale: 1m,
            Offset: 0m,
            BucketSeconds: 60,
            DataType: ModbusRegisterDataType.Float32,
            WordOrder: ModbusWordOrder.BigEndian);

        await client.ConnectAsync(new ModbusConnectionOptions(simulator.Endpoint, null), CancellationToken.None);
        var samples = await client.ReadRegistersAsync(mapping, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(samples);
    }

    private sealed class ModbusTcpSimulator : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;

        private ModbusTcpSimulator(TcpListener listener, Task serverTask)
        {
            _listener = listener;
            _serverTask = serverTask;
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Endpoint = $"tcp://127.0.0.1:{endpoint.Port}";
        }

        public string Endpoint { get; }

        public static Task<ModbusTcpSimulator> StartAsync(
            Func<ModbusRequest, byte[]> createPayload,
            ushort transactionIdOffset = 0,
            byte unitIdOffset = 0)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var serverTask = Task.Run(async () =>
            {
                using var tcpClient = await listener.AcceptTcpClientAsync();
                await using var stream = tcpClient.GetStream();
                var request = new byte[12];
                await ReadExactlyAsync(stream, request);
                var transactionId = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0, 2));
                var modbusRequest = new ModbusRequest(
                    request[6],
                    request[7],
                    BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10, 2)));
                var payload = createPayload(modbusRequest);
                var response = new byte[9 + payload.Length];
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), (ushort)(transactionId + transactionIdOffset));
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(3 + payload.Length));
                response[6] = (byte)(modbusRequest.UnitId + unitIdOffset);
                response[7] = modbusRequest.FunctionCode;
                response[8] = (byte)payload.Length;
                payload.CopyTo(response.AsSpan(9));
                await stream.WriteAsync(response);
            });
            return Task.FromResult(new ModbusTcpSimulator(listener, serverTask));
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset));
                if (read == 0)
                {
                    throw new InvalidOperationException("Client closed the simulator connection.");
                }

                offset += read;
            }
        }
    }

    private sealed record ModbusRequest(byte UnitId, byte FunctionCode, ushort Address, ushort Count);
}
