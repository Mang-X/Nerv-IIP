using DotNetCore.CAP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.Testing;

/// <summary>
/// CAP-aware helpers for tests that boot a real host (typically via <c>WebApplicationFactory&lt;TEntryPoint&gt;</c>)
/// and dispose it moments later.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: DotNetCore.CAP 10.0.1 has a teardown race in
/// <c>Internal/IConsumerRegister.Default.cs</c>. <c>ConsumerRegister.StartAsync</c> ends with
/// <c>_disposed = 0;</c> — it re-arms the dispose guard <em>after</em> <c>ExecuteAsync()</c> completes.
/// When a short-lived test host is disposed while the CAP <c>Bootstrapper</c> background service is still
/// bootstrapping, the sequence is:
/// </para>
/// <list type="number">
/// <item><description>Teardown cancels the bootstrapper CTS; its callback disposes each
/// <c>IProcessingServer</c>. <c>ConsumerRegister.Dispose</c> sets <c>_disposed = 1</c> and
/// <c>Pulse()</c> cancels <em>and disposes</em> the internal <c>CancellationTokenSource</c>.</description></item>
/// <item><description>The in-flight <c>StartAsync</c> then finishes and resets <c>_disposed = 0</c>,
/// reviving the guard while <c>_cts</c> stays disposed.</description></item>
/// <item><description>Container teardown (<c>ServiceProviderEngineScope.DisposeAsync</c>) calls
/// <c>ConsumerRegister.Dispose</c> a second time; the guard passes and <c>Pulse()</c> calls
/// <c>Cancel()</c> on the disposed CTS → <see cref="ObjectDisposedException"/> escapes through
/// <c>WebApplicationFactory.DisposeAsync</c> and fails an arbitrary test.</description></item>
/// </list>
/// <para>
/// Upstream (unfixed as of v10.0.1, latest release):
/// https://github.com/dotnetcore/CAP/blob/v10.0.1/src/DotNetCore.CAP/Internal/IConsumerRegister.Default.cs
/// </para>
/// <para>
/// The fix is to await CAP bootstrap completion right after the host starts (e.g. after the first
/// <c>CreateClient()</c> call): once <c>Bootstrapper.ExecuteAsync</c> has completed, every
/// <c>ConsumerRegister.StartAsync</c> has returned and the guard can no longer be re-armed, so the
/// container's single <c>Dispose</c> runs cleanly. Waiting at host start (rather than just before
/// disposal) also closes the race when a test fails an assertion mid-body and disposal runs early.
/// </para>
/// </remarks>
public static class CapTestHost
{
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Awaits completion of the CAP <c>Bootstrapper</c> background service so that a subsequent host
    /// disposal cannot race CAP consumer startup. No-op when CAP is not registered in the host.
    /// Call once per test host, right after the host has started (e.g. after <c>CreateClient()</c>).
    /// </summary>
    /// <param name="services">The root service provider of the test host (e.g. <c>factory.Services</c>).</param>
    public static async ValueTask WaitForCapBootstrapAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<IBootstrapper>() is not BackgroundService { ExecuteTask: { } executeTask })
        {
            return;
        }

        try
        {
            await executeTask.WaitAsync(BootstrapTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host is already stopping; ExecuteTask has completed (canceled), which still guarantees
            // ConsumerRegister.StartAsync has returned and the dispose guard cannot be re-armed.
        }
    }
}
