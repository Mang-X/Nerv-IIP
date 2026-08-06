using System.Net;
using System.Net.Sockets;

namespace Nerv.IIP.Testing;

/// <summary>
/// A loopback TCP endpoint that was bound and then released, so the kernel answers any connect
/// attempt with an immediate reset. Dialing it is deterministically
/// <see cref="NetworkFailureKind.ConnectionRefused"/> on every machine: no DNS resolver, no
/// firewall policy and no "hopefully this address times out" guesswork is involved.
/// </summary>
public sealed record RefusedTcpEndpoint(IPAddress Address, int Port)
{
    public string Host => Address.ToString();

    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// Explicit network-failure fixtures for tests that need a dependency which cannot be reached.
/// </summary>
public static class NetworkFailureFixture
{
    /// <summary>
    /// Reserves an ephemeral loopback port and closes the listener before returning, so the port
    /// carries no listener while the test runs. A connect attempt therefore fails immediately and
    /// classifies as <see cref="NetworkFailureKind.ConnectionRefused"/> instead of hanging until
    /// some ambient timeout expires.
    /// </summary>
    public static RefusedTcpEndpoint ReserveRefusedLoopbackEndpoint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return new(IPAddress.Loopback, port);
    }
}
