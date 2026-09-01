using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

public sealed class LabelPrinterOptions
{
    public string Mode { get; init; } = "disabled";
    public string? Host { get; init; }
    public int Port { get; init; } = 9100;
    public int ConnectTimeoutSeconds { get; init; } = 10;
    public int WriteTimeoutSeconds { get; init; } = 10;
}

internal interface IZplTcpConnectionFactory
{
    Task<IZplTcpConnection> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface IZplTcpConnection : IAsyncDisposable
{
    ValueTask<int> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    void ShutdownSend();
}

internal sealed class SocketZplTcpConnectionFactory : IZplTcpConnectionFactory
{
    public async Task<IZplTcpConnection> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await client.ConnectAsync(host, port, timeoutSource.Token);
            return new SocketZplTcpConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class SocketZplTcpConnection(TcpClient client) : IZplTcpConnection
    {
        public ValueTask<int> SendAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            client.Client.SendAsync(payload, SocketFlags.None, cancellationToken);

        public void ShutdownSend() => client.Client.Shutdown(SocketShutdown.Send);

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class ZplTcpLabelPrinter : ILabelPrinter
{
    private readonly IOptions<LabelPrinterOptions> options;
    private readonly IZplTcpConnectionFactory connectionFactory;
    private readonly TimeProvider timeProvider;

    public ZplTcpLabelPrinter(IOptions<LabelPrinterOptions> options)
        : this(options, new SocketZplTcpConnectionFactory(), TimeProvider.System)
    {
    }

    internal ZplTcpLabelPrinter(
        IOptions<LabelPrinterOptions> options,
        IZplTcpConnectionFactory connectionFactory)
        : this(options, connectionFactory, TimeProvider.System)
    {
    }

    internal ZplTcpLabelPrinter(
        IOptions<LabelPrinterOptions> options,
        IZplTcpConnectionFactory connectionFactory,
        TimeProvider timeProvider)
    {
        this.options = options;
        this.connectionFactory = connectionFactory;
        this.timeProvider = timeProvider;
    }

    public async Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<CompiledLabelDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0 || documents.Any(document => document.Payload.IsEmpty))
        {
            return LabelPrinterDispatchResult.Failed("未提供可发送的已编译标签文档。");
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            return LabelPrinterDispatchResult.Failed("未配置 ZPL TCP 打印机地址。");
        }

        if (settings.Port is <= 0 or > 65535
            || settings.ConnectTimeoutSeconds <= 0
            || settings.WriteTimeoutSeconds <= 0)
        {
            return LabelPrinterDispatchResult.Failed("ZPL TCP 打印机端口或超时配置无效。");
        }

        var printJobId = $"zpl-{Guid.CreateVersion7():N}";
        long confirmedBytesWritten = 0;
        try
        {
            await using var connection = await connectionFactory.ConnectAsync(
                settings.Host,
                settings.Port,
                TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds),
                cancellationToken);
            using var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromSeconds(settings.WriteTimeoutSeconds),
                timeProvider);
            using var transferSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            foreach (var document in documents)
            {
                var remaining = document.Payload;
                while (!remaining.IsEmpty)
                {
                    var written = await connection.SendAsync(remaining, transferSource.Token);
                    if (written <= 0 || written > remaining.Length)
                    {
                        throw new IOException("The TCP socket returned an invalid write count.");
                    }

                    confirmedBytesWritten += written;
                    remaining = remaining[written..];
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            connection.ShutdownSend();
            return LabelPrinterDispatchResult.Sent(printJobId);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            var attemptResult = confirmedBytesWritten > 0
                ? LabelPrinterDispatchResult.DeliveryUnknown(
                    printJobId,
                    "TCP 写入已开始但未确认完整交付，禁止自动重试。")
                : LabelPrinterDispatchResult.Failed("TCP 传输在首字节写入前失败。");
            throw new LabelPrinterDispatchCanceledException(
                attemptResult,
                exception,
                cancellationToken);
        }
        catch (Exception)
        {
            return confirmedBytesWritten > 0
                ? LabelPrinterDispatchResult.DeliveryUnknown(
                    printJobId,
                    "TCP 写入已开始但未确认完整交付，禁止自动重试。")
                : LabelPrinterDispatchResult.Failed("TCP 传输在首字节写入前失败。");
        }
    }
}

public sealed class ConfiguredLabelPrinter(
    IOptions<LabelPrinterOptions> options,
    ZplTcpLabelPrinter zplPrinter,
    IHostEnvironment environment)
    : ILabelPrinter
{
    public Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<CompiledLabelDocument> documents,
        CancellationToken cancellationToken)
    {
        return options.Value.Mode.Trim().ToLowerInvariant() switch
        {
            "zpl-tcp" => zplPrinter.PrintAsync(printerId, documents, cancellationToken),
            "simulated" when environment.IsDevelopment()
                || string.Equals(environment.EnvironmentName, "Testing", StringComparison.Ordinal) =>
                Task.FromResult(LabelPrinterDispatchResult.Sent($"sim-{Guid.CreateVersion7():N}")),
            "simulated" => Task.FromResult(
                LabelPrinterDispatchResult.Failed("模拟打印模式仅允许在 Development 或 Testing 环境使用。")),
            _ => Task.FromResult(LabelPrinterDispatchResult.Failed("标签打印机未启用。")),
        };
    }
}
