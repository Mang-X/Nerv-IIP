using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class ZplTcpLabelPrinterTests
{
    [Fact]
    public async Task Zpl_tcp_printer_sends_a_complete_zpl_document_and_truthfully_reports_sent_to_printer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = ReceiveAsync(listener);
        var printer = new ZplTcpLabelPrinter(Options.Create(new LabelPrinterOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ConnectTimeoutSeconds = 5,
        }));

        var result = await printer.PrintAsync("printer-zpl-01", ["LABEL-001"], CancellationToken.None);
        var zpl = await received;

        Assert.Equal("sent-to-printer", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.PrintJobId));
        Assert.Null(result.FailureReason);
        Assert.Contains("^XA", zpl, StringComparison.Ordinal);
        Assert.Contains("^FDLABEL-001^FS", zpl, StringComparison.Ordinal);
        Assert.Contains("^XZ", zpl, StringComparison.Ordinal);
    }

    private static async Task<string> ReceiveAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var buffer = new MemoryStream();
        var bytes = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(bytes)) > 0)
        {
            await buffer.WriteAsync(bytes.AsMemory(0, read));
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
