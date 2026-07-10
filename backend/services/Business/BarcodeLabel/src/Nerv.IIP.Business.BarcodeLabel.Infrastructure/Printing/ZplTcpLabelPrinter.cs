using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

public sealed class LabelPrinterOptions
{
    public string Mode { get; init; } = "disabled";

    public string? Host { get; init; }

    public int Port { get; init; } = 9100;

    public int ConnectTimeoutSeconds { get; init; } = 10;
}

public sealed class ZplTcpLabelPrinter(IOptions<LabelPrinterOptions> options)
    : ILabelPrinter
{
    public async Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<string> labelValues,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(labelValues);
        if (labelValues.Count == 0)
        {
            return LabelPrinterDispatchResult.Failed("No labels were supplied for printing.");
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            return LabelPrinterDispatchResult.Failed("LabelPrinter:Host is required for ZPL-over-TCP printing.");
        }

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            await using var stream = client.GetStream();
            var document = BuildDocument(labelValues);
            var payload = Encoding.UTF8.GetBytes(document);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            client.Client.Shutdown(SocketShutdown.Send);
            return LabelPrinterDispatchResult.Sent($"zpl-{Guid.CreateVersion7():N}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LabelPrinterDispatchResult.Failed("Timed out connecting to the ZPL printer.");
        }
        catch (SocketException exception)
        {
            return LabelPrinterDispatchResult.Failed($"ZPL printer connection failed: {exception.SocketErrorCode}.");
        }
        catch (IOException exception)
        {
            return LabelPrinterDispatchResult.Failed($"ZPL printer write failed: {exception.Message}");
        }
    }

    private static string BuildDocument(IEnumerable<string> labelValues)
    {
        return string.Concat(labelValues.Select(labelValue => $"^XA^FO20,20^A0N,30,30^FD{Escape(labelValue)}^FS^XZ"));
    }

    private static string Escape(string labelValue)
    {
        return labelValue
            .Replace("^", " ", StringComparison.Ordinal)
            .Replace("~", " ", StringComparison.Ordinal);
    }
}

public sealed class ConfiguredLabelPrinter(IOptions<LabelPrinterOptions> options, ZplTcpLabelPrinter zplPrinter)
    : ILabelPrinter
{
    public Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<string> labelValues,
        CancellationToken cancellationToken)
    {
        return options.Value.Mode.Trim().ToLowerInvariant() switch
        {
            "zpl-tcp" => zplPrinter.PrintAsync(printerId, labelValues, cancellationToken),
            "simulated" => Task.FromResult(LabelPrinterDispatchResult.Printed($"sim-{Guid.CreateVersion7():N}")),
            _ => Task.FromResult(LabelPrinterDispatchResult.Failed("Label printer is disabled. Configure LabelPrinter:Mode as zpl-tcp or simulated.")),
        };
    }
}
