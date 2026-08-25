using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class ZplTcpLabelPrinterTests
{
    [Fact]
    public async Task Full_write_sends_exact_compiled_bytes_half_closes_and_reports_sent_to_printer()
    {
        await TestTimeout.RunAsync(
            "ZPL loopback transfer and EOF observation",
            async cancellationToken =>
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var received = ReceiveUntilEofAsync(listener, cancellationToken);
                var printer = new ZplTcpLabelPrinter(Options.Create(new LabelPrinterOptions
                {
                    Host = IPAddress.Loopback.ToString(),
                    Port = port,
                    ConnectTimeoutSeconds = 5,
                    WriteTimeoutSeconds = 5,
                }));
                var documents = CompileDocuments(2);

                var result = await printer.PrintAsync("printer-zpl-01", documents, cancellationToken);
                var payload = await received;

                Assert.Equal("sent-to-printer", result.Status);
                Assert.False(string.IsNullOrWhiteSpace(result.PrintJobId));
                Assert.Null(result.FailureReason);
                Assert.Equal(documents.SelectMany(document => document.Payload.ToArray()).ToArray(), payload);
            },
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Write_timeout_is_a_single_budget_across_all_partial_writes()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var connection = new BudgetAwareScriptedConnection(clock);
        var factory = new ScriptedConnectionFactory { Connection = connection };
        var printer = new ZplTcpLabelPrinter(Options.Create(new LabelPrinterOptions
        {
            Host = "printer.test",
            Port = 9100,
            ConnectTimeoutSeconds = 1,
            WriteTimeoutSeconds = 1,
        }), factory, clock);

        var result = await printer.PrintAsync("printer-01", CompileDocuments(1), CancellationToken.None);

        Assert.Equal("delivery-unknown", result.Status);
        Assert.Equal(2, connection.SendCalls);
        Assert.False(connection.ShutdownSendCalled);
    }

    [Fact]
    public async Task Loopback_receive_observes_cancellation_before_an_independent_watchdog()
    {
        await TestTimeout.RunAsync(
            "ZPL loopback cancellation probe including connect, accept, and read",
            async testCancellationToken =>
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var client = new TcpClient();
                var accept = listener.AcceptTcpClientAsync(testCancellationToken).AsTask();
                await client.ConnectAsync(IPAddress.Loopback, port, testCancellationToken);
                using var serverPeer = await accept;

                using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellationToken);
                var receive = ReadUntilEofAsync(serverPeer, receiveCancellation.Token);
                receiveCancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    receive.WaitAsync(TimeSpan.FromSeconds(1), testCancellationToken));
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Failure_before_the_first_byte_reports_failed()
    {
        var factory = new ScriptedConnectionFactory { ConnectFailure = new SocketException((int)SocketError.ConnectionRefused) };
        var result = await CreateScriptedPrinter(factory).PrintAsync("printer-01", CompileDocuments(1), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Null(result.PrintJobId);
        Assert.Equal(1, factory.ConnectCalls);
    }

    [Fact]
    public async Task Helper_timeout_before_the_first_byte_reports_failed()
    {
        var connection = new ScriptedConnection(_ => throw new OperationCanceledException("scripted timeout"));
        var factory = new ScriptedConnectionFactory { Connection = connection };

        var result = await CreateScriptedPrinter(factory).PrintAsync("printer-01", CompileDocuments(1), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal(1, factory.ConnectCalls);
        Assert.Equal(1, connection.SendCalls);
    }

    [Fact]
    public async Task Failure_after_a_partial_write_reports_delivery_unknown_without_retrying()
    {
        var connection = new ScriptedConnection(buffer => Math.Min(3, buffer.Length), _ => throw new IOException("scripted disconnect"));
        var factory = new ScriptedConnectionFactory { Connection = connection };
        var result = await CreateScriptedPrinter(factory).PrintAsync("printer-01", CompileDocuments(1), CancellationToken.None);

        Assert.Equal("delivery-unknown", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.PrintJobId));
        Assert.Equal(1, factory.ConnectCalls);
        Assert.Equal(2, connection.SendCalls);
        Assert.False(connection.ShutdownSendCalled);
    }

    [Fact]
    public async Task Caller_cancellation_before_the_first_byte_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new ScriptedConnectionFactory { HonorCancellationDuringConnect = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScriptedPrinter(factory).PrintAsync(
            "printer-01", CompileDocuments(1), cancellation.Token));

        Assert.Equal(1, factory.ConnectCalls);
    }

    [Fact]
    public async Task Caller_cancellation_after_the_first_byte_reports_delivery_unknown_without_retrying()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new ScriptedConnection(
            buffer => { cancellation.Cancel(); return Math.Min(1, buffer.Length); },
            _ => throw new OperationCanceledException(cancellation.Token));
        var factory = new ScriptedConnectionFactory { Connection = connection };

        var result = await CreateScriptedPrinter(factory).PrintAsync("printer-01", CompileDocuments(1), cancellation.Token);

        Assert.Equal("delivery-unknown", result.Status);
        Assert.Equal(1, factory.ConnectCalls);
        Assert.Equal(2, connection.SendCalls);
    }

    [Fact]
    public async Task Simulated_mode_is_rejected_in_production()
    {
        var options = Options.Create(new LabelPrinterOptions { Mode = "simulated" });
        var printer = new ConfiguredLabelPrinter(options, new ZplTcpLabelPrinter(options), new TestHostEnvironment("Production"));

        var result = await printer.PrintAsync("printer-01", CompileDocuments(1), CancellationToken.None);

        Assert.Equal("failed", result.Status);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Simulated_mode_only_reports_sent_to_printer_in_explicit_nonproduction_environments(string environmentName)
    {
        var options = Options.Create(new LabelPrinterOptions { Mode = "simulated" });
        var printer = new ConfiguredLabelPrinter(options, new ZplTcpLabelPrinter(options), new TestHostEnvironment(environmentName));

        var result = await printer.PrintAsync("printer-01", CompileDocuments(1), CancellationToken.None);

        Assert.Equal("sent-to-printer", result.Status);
    }

    private static ZplTcpLabelPrinter CreateScriptedPrinter(ScriptedConnectionFactory factory) =>
        new(Options.Create(new LabelPrinterOptions
        {
            Host = "printer.test",
            Port = 9100,
            ConnectTimeoutSeconds = 1,
            WriteTimeoutSeconds = 1,
        }), factory);

    private static IReadOnlyCollection<CompiledLabelDocument> CompileDocuments(int count)
    {
        const string templateJson =
            """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
        var template = LabelTemplateDocument.Parse(templateJson);
        var schema = LabelVariableSchema.Parse("""{"version":1,"variables":[]}""");
        var items = Enumerable.Range(1, count)
            .Select(sequence => new LabelCompilationItem("{}", new LabelReservedVariables($"LABEL-{sequence:000}", null, sequence, "ASN-001")))
            .ToArray();
        return ZplV1LabelCompiler.CompileBatch(template, schema, "code128", items);
    }

    private static async Task<byte[]> ReceiveUntilEofAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        return await ReadUntilEofAsync(client, cancellationToken);
    }

    private static async Task<byte[]> ReadUntilEofAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var buffer = new MemoryStream();
        var bytes = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(bytes, cancellationToken)) > 0)
        {
            await buffer.WriteAsync(bytes.AsMemory(0, read));
        }

        return buffer.ToArray();
    }

    private sealed class ScriptedConnectionFactory : IZplTcpConnectionFactory
    {
        public int ConnectCalls { get; private set; }
        public Exception? ConnectFailure { get; init; }
        public bool HonorCancellationDuringConnect { get; init; }
        public IZplTcpConnection Connection { get; init; } = new ScriptedConnection(buffer => buffer.Length);

        public Task<IZplTcpConnection> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ConnectCalls++;
            if (HonorCancellationDuringConnect) cancellationToken.ThrowIfCancellationRequested();
            if (ConnectFailure is not null) throw ConnectFailure;
            return Task.FromResult<IZplTcpConnection>(Connection);
        }
    }

    private sealed class BudgetAwareScriptedConnection(TimerRegistrationObservingTimeProvider clock) : IZplTcpConnection
    {
        public int SendCalls { get; private set; }
        public bool ShutdownSendCalled { get; private set; }

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            SendCalls++;
            clock.Advance(
                SendCalls == 1 ? TimeSpan.FromMilliseconds(800) : TimeSpan.FromMilliseconds(300));
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SendCalls == 1 ? Math.Min(1, payload.Length) : payload.Length);
        }

        public void ShutdownSend() => ShutdownSendCalled = true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ScriptedConnection(params Func<ReadOnlyMemory<byte>, int>[] steps) : IZplTcpConnection
    {
        private readonly Queue<Func<ReadOnlyMemory<byte>, int>> remainingSteps = new(steps);
        public int SendCalls { get; private set; }
        public bool ShutdownSendCalled { get; private set; }

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            SendCalls++;
            return ValueTask.FromResult(remainingSteps.Count == 0 ? payload.Length : remainingSteps.Dequeue()(payload));
        }

        public void ShutdownSend() => ShutdownSendCalled = true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BarcodeLabel.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
