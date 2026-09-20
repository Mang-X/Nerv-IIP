using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

public sealed class LabelPrinterOptions
{
    public string Mode { get; init; } = string.Empty;
    public List<LabelPrinterRouteOptions> Printers { get; init; } = [];
}

public sealed record LabelPrinterRouteOptions
{
    public string Id { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public int ConnectTimeoutSeconds { get; init; }
    public int WriteTimeoutSeconds { get; init; }
    public int Dpi { get; init; }
    public string Language { get; init; } = string.Empty;
    public string Capabilities { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed class LabelPrinterOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<LabelPrinterOptions>
{
    private static readonly HashSet<int> SupportedDpi = [203, 300, 600];
    private static readonly HashSet<string> SupportedCapabilities =
    [
        "code128",
        "gs1-128",
        "qr",
        "datamatrix",
        "gs1-datamatrix",
    ];

    public ValidateOptionsResult Validate(string? name, LabelPrinterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Mode))
        {
            return ValidateOptionsResult.Fail("LabelPrinter:Mode must be configured explicitly.");
        }

        var mode = options.Mode.Trim().ToLowerInvariant();
        if (mode is not ("simulated" or "zpl-tcp"))
        {
            return ValidateOptionsResult.Fail(
                "LabelPrinter:Mode must be either 'simulated' or 'zpl-tcp'.");
        }

        if (mode == "simulated"
            && !environment.IsDevelopment()
            && !environment.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Fail(
                "LabelPrinter:Mode=simulated is only allowed in Development or Testing.");
        }

        if (mode == "simulated")
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.Printers.Count == 0)
        {
            failures.Add("LabelPrinter:Printers must contain at least one route in zpl-tcp mode.");
        }

        var duplicateIds = options.Printers
            .Where(route => !string.IsNullOrWhiteSpace(route.Id))
            .GroupBy(route => route.Id.Trim(), StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            failures.Add($"LabelPrinter:Printers contains duplicate printer id(s): {string.Join(", ", duplicateIds)}.");
        }

        for (var index = 0; index < options.Printers.Count; index++)
        {
            ValidateRoute(options.Printers[index], index, failures);
        }

        if (options.Printers.Count > 0 && !options.Printers.Any(route => route.Enabled))
        {
            failures.Add("LabelPrinter:Printers must contain at least one enabled route.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRoute(
        LabelPrinterRouteOptions route,
        int index,
        List<string> failures)
    {
        var path = $"LabelPrinter:Printers:{index}";
        if (!IsPrinterId(route.Id))
        {
            failures.Add($"{path}:Id must be 1-100 characters using letters, digits, '.', '_' or '-'.");
        }

        if (string.IsNullOrWhiteSpace(route.Host)
            || !string.Equals(route.Host, route.Host.Trim(), StringComparison.Ordinal)
            || Uri.CheckHostName(route.Host) == UriHostNameType.Unknown)
        {
            failures.Add($"{path}:Host must be a DNS name or IP address without a scheme or port.");
        }

        if (route.Port is <= 0 or > 65535)
        {
            failures.Add($"{path}:Port must be between 1 and 65535.");
        }

        if (route.ConnectTimeoutSeconds <= 0)
        {
            failures.Add($"{path}:ConnectTimeoutSeconds must be positive.");
        }

        if (route.WriteTimeoutSeconds <= 0)
        {
            failures.Add($"{path}:WriteTimeoutSeconds must be positive.");
        }

        if (!SupportedDpi.Contains(route.Dpi))
        {
            failures.Add($"{path}:Dpi must be one of 203, 300, or 600.");
        }

        if (!string.Equals(route.Language, "zpl", StringComparison.Ordinal))
        {
            failures.Add($"{path}:Language must be 'zpl'.");
        }

        var capabilities = route.Capabilities.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (capabilities.Length == 0
            || capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Length
            || capabilities.Any(capability => !SupportedCapabilities.Contains(capability)))
        {
            failures.Add(
                $"{path}:Capabilities must be a unique comma-separated subset of code128, gs1-128, qr, datamatrix, gs1-datamatrix.");
        }
    }

    private static bool IsPrinterId(string value) =>
        value.Length is > 0 and <= 100
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
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

        var settings = options.Value.Printers.SingleOrDefault(
            route => string.Equals(route.Id, printerId, StringComparison.Ordinal));
        if (settings is null)
        {
            return LabelPrinterDispatchResult.Failed($"未配置标签打印机 '{printerId}'。");
        }

        if (!settings.Enabled)
        {
            return LabelPrinterDispatchResult.Failed($"标签打印机 '{printerId}' 已禁用。");
        }

        var capabilities = settings.Capabilities.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (documents.Any(document => document.Dpi != settings.Dpi
            || !capabilities.Contains(document.Capability, StringComparer.Ordinal)))
        {
            return LabelPrinterDispatchResult.Failed(
                $"标签打印机 '{printerId}' 不支持待发送标签的 DPI 或条码类型。");
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
