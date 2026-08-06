namespace Nerv.IIP.Testing.Tests;

public sealed class NetworkFailureFixtureTests
{
    // Connection budget and request budget are configured separately on purpose: one governs
    // establishing the transport, the other governs the exchange once it exists. Neither is a
    // stand-in for the other, and against a refused endpoint neither is expected to elapse.
    private static readonly TimeSpan ConnectBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Reserved_loopback_endpoint_is_classified_as_connection_refused()
    {
        var refused = NetworkFailureFixture.ReserveRefusedLoopbackEndpoint();
        using var client = CreateClient();

        var exception = await Record.ExceptionAsync(() => client.GetAsync(Uri(refused)));

        Assert.NotNull(exception);
        var observation = NetworkFailureClassifier.FromException(exception!, CancellationToken.None);
        Assert.Equal(NetworkFailureKind.ConnectionRefused, observation.Kind);
        Assert.Equal("Connection was refused.", observation.Diagnostic);
    }

    [Fact]
    public void Reserved_loopback_endpoints_do_not_collide()
    {
        var endpoints = Enumerable.Range(0, 32)
            .Select(_ => NetworkFailureFixture.ReserveRefusedLoopbackEndpoint())
            .ToArray();

        Assert.All(endpoints, endpoint => Assert.Equal("127.0.0.1", endpoint.Host));
        Assert.All(endpoints, endpoint => Assert.InRange(endpoint.Port, 1024, 65535));
        Assert.Equal(endpoints.Length, endpoints.Select(endpoint => endpoint.Port).Distinct().Count());
    }

    [Fact]
    public async Task Caller_cancellation_against_the_fixture_is_not_rewritten_as_a_request_timeout()
    {
        var refused = NetworkFailureFixture.ReserveRefusedLoopbackEndpoint();
        using var client = CreateClient();
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();

        var exception = await Record.ExceptionAsync(() => client.GetAsync(Uri(refused), callerCancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        var rethrown = Assert.ThrowsAny<OperationCanceledException>(() =>
            NetworkFailureClassifier.FromException(exception!, callerCancellation.Token));
        Assert.Same(exception, rethrown);
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = ConnectBudget };
        return new HttpClient(handler, disposeHandler: true) { Timeout = RequestBudget };
    }

    private static Uri Uri(RefusedTcpEndpoint endpoint) =>
        new($"http://{endpoint.Host}:{endpoint.Port}/");
}
