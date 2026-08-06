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
    public void Simultaneously_reserved_loopback_endpoints_do_not_collide()
    {
        // 断言的对象是**同时持有**的一批预留：读取端口的那一刻 32 个 listener 全部处于 bound 状态，
        // 内核不会把同一个 addr:port 再交给第二次 bind(0)，因此互异是构造上的必然。
        //
        // 这里刻意不对**连续**预留断言互异：单个预留在返回前就已释放端口，OS 并不承诺跳过刚回到
        // ephemeral 池的端口。那样的断言是把无保证行为当契约，属于自造抖动源；而它本来要防的
        // 「夹具把同一个端口发两次」在这一批里已被完整覆盖。
        const int Count = 32;

        var endpoints = NetworkFailureFixture.ReserveRefusedLoopbackEndpoints(Count);

        Assert.Equal(Count, endpoints.Count);
        Assert.All(endpoints, endpoint => Assert.Equal("127.0.0.1", endpoint.Host));
        Assert.All(endpoints, endpoint => Assert.InRange(endpoint.Port, 1024, 65535));
        Assert.Equal(Count, endpoints.Select(endpoint => endpoint.Port).Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserving_a_non_positive_batch_is_rejected(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetworkFailureFixture.ReserveRefusedLoopbackEndpoints(count));
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
