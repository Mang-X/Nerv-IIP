using System.Buffers.Binary;
using System.Net.Sockets;

namespace Nerv.IIP.ConnectorHost.Connectors.Modbus;

public sealed class ModbusTcpClient : IModbusTcpClient, IDisposable
{
    private readonly SemaphoreSlim _protocolGate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;
    private string? _connectedEndpoint;

    public async Task ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken)
    {
        await _protocolGate.WaitAsync(cancellationToken);
        try
        {
            if (_client?.Connected == true && string.Equals(_connectedEndpoint, options.Endpoint, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeClient();
            var endpoint = ParseEndpoint(options.Endpoint);
            _client = new TcpClient();
            await _client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
            _stream = _client.GetStream();
            _connectedEndpoint = options.Endpoint;
        }
        finally
        {
            _protocolGate.Release();
        }
    }

    public async Task<IReadOnlyList<ModbusRegisterSample>> ReadRegistersAsync(
        ModbusRegisterMapping mapping,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await _protocolGate.WaitAsync(cancellationToken);
        try
        {
            return await ReadRegistersCoreAsync(mapping, observedAtUtc, cancellationToken);
        }
        finally
        {
            _protocolGate.Release();
        }
    }

    public async Task ProbeAsync(ModbusRegisterMapping mapping, CancellationToken cancellationToken)
    {
        await _protocolGate.WaitAsync(cancellationToken);
        try
        {
            _ = await ReadRegistersCoreAsync(mapping, DateTimeOffset.UtcNow, cancellationToken);
        }
        finally
        {
            _protocolGate.Release();
        }
    }

    private async Task<IReadOnlyList<ModbusRegisterSample>> ReadRegistersCoreAsync(
        ModbusRegisterMapping mapping,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Modbus TCP client is not connected.");
        }

        var functionCode = mapping.Table == ModbusRegisterTable.HoldingRegisters ? (byte)0x03 : (byte)0x04;
        var protocolAddress = ToProtocolAddress(mapping.Table, mapping.Address);
        var request = new byte[12];
        var transactionId = ++_transactionId;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = mapping.UnitId;
        request[7] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), protocolAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), mapping.RegisterCount);

        await _stream.WriteAsync(request, cancellationToken);

        var header = new byte[7];
        await ReadExactlyAsync(_stream, header, cancellationToken);
        var responseTransactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
        if (responseTransactionId != transactionId)
        {
            throw new InvalidOperationException("Modbus TCP response transaction id does not match the request.");
        }

        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        if (protocolId != 0)
        {
            throw new InvalidOperationException("Modbus TCP response protocol id is invalid.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (length < 3)
        {
            throw new InvalidOperationException("Modbus TCP response length is invalid.");
        }

        if (header[6] != mapping.UnitId)
        {
            throw new InvalidOperationException("Modbus TCP response unit id does not match the request.");
        }

        var body = new byte[length - 1];
        await ReadExactlyAsync(_stream, body, cancellationToken);
        if (body.Length < 2 || body[0] != functionCode)
        {
            throw new InvalidOperationException($"Modbus TCP read failed with function code 0x{body.ElementAtOrDefault(0):X2}.");
        }

        var byteCount = body[1];
        var expectedByteCount = mapping.RegisterCount * 2;
        if (body.Length < 2 + byteCount || byteCount != expectedByteCount)
        {
            throw new InvalidOperationException("Modbus TCP read returned an invalid register payload.");
        }

        var value = ConvertRegisters(body.AsSpan(2, byteCount), mapping.DataType, mapping.WordOrder);
        return value is null
            ? []
            : [new ModbusRegisterSample(mapping.UnitId, mapping.Table, mapping.Address, value.Value, observedAtUtc)];
    }

    public void Dispose()
    {
        DisposeClient();
        _protocolGate.Dispose();
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Modbus TCP connection closed while reading response.");
            }

            offset += read;
        }
    }

    private static decimal? ConvertRegisters(ReadOnlySpan<byte> bytes, ModbusRegisterDataType dataType, ModbusWordOrder wordOrder)
    {
        if (dataType is ModbusRegisterDataType.UInt16 or ModbusRegisterDataType.Int16 && bytes.Length != 2)
        {
            throw new InvalidOperationException($"{dataType} requires exactly one Modbus register.");
        }

        if (dataType is ModbusRegisterDataType.UInt32 or ModbusRegisterDataType.Int32 or ModbusRegisterDataType.Float32 && bytes.Length != 4)
        {
            throw new InvalidOperationException($"{dataType} requires exactly two Modbus registers.");
        }

        Span<byte> normalized = stackalloc byte[bytes.Length];
        bytes.CopyTo(normalized);
        if (wordOrder == ModbusWordOrder.LittleEndian && normalized.Length == 4)
        {
            (normalized[0], normalized[2]) = (normalized[2], normalized[0]);
            (normalized[1], normalized[3]) = (normalized[3], normalized[1]);
        }

        return dataType switch
        {
            ModbusRegisterDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(normalized),
            ModbusRegisterDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(normalized),
            ModbusRegisterDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(normalized),
            ModbusRegisterDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(normalized),
            ModbusRegisterDataType.Float32 => ConvertFloat32(normalized),
            _ => throw new InvalidOperationException($"Unsupported Modbus register data type '{dataType}'.")
        };
    }

    private static decimal? ConvertFloat32(ReadOnlySpan<byte> bytes)
    {
        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes));
        return float.IsNaN(value) || float.IsInfinity(value)
            ? null
            : Convert.ToDecimal(value);
    }

    private static ushort ToProtocolAddress(ModbusRegisterTable table, ushort address)
    {
        var baseAddress = table == ModbusRegisterTable.HoldingRegisters ? 40001 : 30001;
        return address >= baseAddress ? checked((ushort)(address - baseAddress)) : address;
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var uri = endpoint.Contains("://", StringComparison.Ordinal)
            ? new Uri(endpoint)
            : new Uri($"tcp://{endpoint}");
        return (uri.Host, uri.Port > 0 ? uri.Port : 502);
    }

    private void DisposeClient()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }
}
