using System.Net.Http.Headers;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// Exclusion gate between <em>host construction</em> and <em>request execution</em> inside this
/// assembly.
/// </summary>
/// <remarks>
/// <para>
/// <c>Program.cs</c> calls <c>app.UseFastEndpoints(c =&gt; c.Serializer.Options.Converters.Add(...))</c>.
/// FastEndpoints keeps that configuration in process-wide static state, so building a host mutates
/// global state that concurrently running hosts are reading. That single fact — not the business
/// tests themselves — is why this assembly used to disable xUnit parallelization wholesale.
/// </para>
/// <para>
/// This gate replaces the assembly-wide serialization with the narrow guarantee that is actually
/// required: a host may only be built while <em>no</em> gateway request is in flight, and requests
/// may only run while <em>no</em> host is being built. Requests still run fully parallel with each
/// other. This is a real exclusion mechanism, not a claim that FastEndpoints static state is
/// restorable — nothing here restores it.
/// </para>
/// <para>
/// The gate is a writer-preferring-free multi-reader/single-writer semaphore: a request takes one
/// permit, a host build takes every permit. Host construction never happens while a permit is held
/// (leases are created outside request execution), so the build can acquire permits one by one
/// without deadlocking; concurrent builders are serialized by <see cref="BuildMutex"/> first.
/// </para>
/// </remarks>
internal static class BusinessGatewayTestHostGate
{
    /// <summary>
    /// Upper bound on concurrently executing gateway requests. Comfortably above any realistic
    /// xUnit <c>MaxParallelThreads</c>; excess requests simply queue instead of failing.
    /// </summary>
    private static readonly int Capacity = Math.Max(256, Environment.ProcessorCount * 32);

    private static readonly SemaphoreSlim Permits = new(Capacity, Capacity);
    private static readonly Lock BuildMutex = new();

    /// <summary>
    /// Runs <paramref name="build"/> with every request permit held, i.e. with no gateway request
    /// in flight anywhere in the assembly.
    /// </summary>
    internal static T Build<T>(Func<T> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        lock (BuildMutex)
        {
            for (var i = 0; i < Capacity; i++)
            {
                Permits.Wait();
            }

            try
            {
                return build();
            }
            finally
            {
                Permits.Release(Capacity);
            }
        }
    }

    /// <summary>
    /// Client-side handler that holds one request permit for the duration of the exchange.
    /// </summary>
    internal sealed class RequestPermitHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Permits.WaitAsync(cancellationToken);
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                Permits.Release();
            }
        }
    }

    /// <summary>
    /// Applies the scope header used by <see cref="BusinessGatewayTestHost"/> to route downstream
    /// fakes to the owning lease.
    /// </summary>
    internal static void ApplyScopeHeader(HttpHeaders headers, string scopeId)
    {
        headers.Remove(BusinessGatewayTestHost.ScopeHeader);
        headers.Add(BusinessGatewayTestHost.ScopeHeader, scopeId);
    }
}
