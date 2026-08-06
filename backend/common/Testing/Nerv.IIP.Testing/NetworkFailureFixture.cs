using System.Net;
using System.Net.Sockets;

namespace Nerv.IIP.Testing;

/// <summary>
/// A loopback TCP endpoint that was bound and then released, so that — as long as nothing else has
/// taken the port — the kernel answers a connect attempt with an immediate reset, which classifies
/// as <see cref="NetworkFailureKind.ConnectionRefused"/>.
///
/// What this buys over a hard-coded address such as <c>127.0.0.1:1</c>: no DNS resolver and no
/// firewall policy participate, so the verdict does not depend on how the host is configured. What
/// it does <b>not</b> buy: an absolute guarantee. See the known race documented on
/// <see cref="NetworkFailureFixture.ReserveRefusedLoopbackEndpoint"/>.
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
    ///
    /// <para><b>Known race — acknowledged debt, not a guarantee.</b> Closing the listener returns
    /// the port to the ephemeral pool, so between this call and the test's connect attempt any other
    /// process on the machine can bind it. If that happens the connect succeeds (or fails some other
    /// way) and the test's premise silently changes. Callers must therefore reserve per test rather
    /// than hold one endpoint in <c>static</c> state for a whole class, which would stretch the
    /// window across every test in it.</para>
    ///
    /// <para><b>Why the obvious fix does not work.</b> Keeping the socket bound but never calling
    /// <c>Listen()</c> would hold the port and close the window — on Linux. It is not portable:
    /// on macOS/BSD a bound, non-listening PCB sits in <c>TCPS_CLOSED</c> and <c>tcp_input</c>
    /// silently <em>drops</em> the SYN instead of resetting it. Measured on Darwin 25.5.0:
    /// bind-only connect timed out after the full budget, while bind+listen+close was refused in
    /// 0.00 s. Switching to bind-without-listen would turn an immediate
    /// <see cref="NetworkFailureKind.ConnectionRefused"/> into a budget-length hang classified as
    /// <see cref="NetworkFailureKind.RequestTimeout"/> — reinstating exactly the vague
    /// "it'll probably time out" semantics this fixture exists to remove.</para>
    ///
    /// <para>A resident listener that resets accepted connections via <c>SO_LINGER(0)</c> would hold
    /// the port with no race, at the cost of yielding <c>ConnectionReset</c> rather than
    /// <c>ConnectionRefused</c> — a change to the four-way split that has to be mirrored into the
    /// production classifier and the docs table. Tracked with the identical "reserve then release"
    /// defect in <c>LoopbackPlatform.ReservePort</c> under issue #1477; fix both together.</para>
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
