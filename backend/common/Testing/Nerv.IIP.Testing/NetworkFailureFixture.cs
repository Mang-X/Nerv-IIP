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
    public static RefusedTcpEndpoint ReserveRefusedLoopbackEndpoint() =>
        ReserveRefusedLoopbackEndpoints(1)[0];

    /// <summary>
    /// Reserves <paramref name="count"/> refused loopback endpoints in one batch: every listener is
    /// bound before any port is read, and all of them are released together afterwards. Each returned
    /// endpoint carries the same semantics as
    /// <see cref="ReserveRefusedLoopbackEndpoint"/>, including the same acknowledged release race.
    ///
    /// <para><b>Why batch at all.</b> While the listeners are bound the kernel will not hand the same
    /// <c>addr:port</c> to a second <c>bind(0)</c>, so the ports in one batch are distinct <em>by
    /// construction</em>. Distinctness across <em>sequential</em> reservations is not a contract and
    /// must not be asserted as one: this method releases each port before returning, and no OS
    /// promises to skip a port that just went back into the ephemeral pool.</para>
    /// </summary>
    public static IReadOnlyList<RefusedTcpEndpoint> ReserveRefusedLoopbackEndpoints(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var listeners = new List<TcpListener>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listeners.Add(listener);
                listener.Start();
            }

            // Read every port while every listener is still bound — that simultaneity is what makes
            // the ports distinct.
            return listeners
                .Select(listener => new RefusedTcpEndpoint(
                    IPAddress.Loopback,
                    ((IPEndPoint)listener.LocalEndpoint).Port))
                .ToArray();
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Stop();
            }
        }
    }
}
