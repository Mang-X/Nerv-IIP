using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

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
/// The permit is held <em>server side</em>, by <see cref="RequestPermitStartupFilter"/>'s outermost
/// middleware, for exactly the span of the server pipeline. Holding it client side (in a
/// <see cref="DelegatingHandler"/> around <c>base.SendAsync</c>) would not be sound: TestServer's
/// <c>ClientHandler</c> returns as soon as the response headers are flushed while the server keeps
/// writing the body, and <see cref="HttpClient"/>'s <c>ResponseContentRead</c> buffering happens
/// outside the handler chain. That leaves a window where the permit is back but server code is
/// still running — precisely the race this gate exists to exclude. See
/// <c>BusinessGatewaySharedHostIsolationTests.Host_construction_waits_for_a_response_body_that_is_still_being_written</c>,
/// which fails against the client-side variant.
/// </para>
/// <para>
/// A permit is therefore only ever held by the server pipeline, never by a test thread, so a host
/// build (which takes every permit) can never be blocked by the thread that requested it. Concurrent
/// builders are serialized by <see cref="BuildMutex"/> first.
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
    /// Upper bound on how long <see cref="Build{T}"/> waits for every request permit. Requests that
    /// are merely queued behind other requests drain in milliseconds, so this is slack for the
    /// pipeline tail rather than a timing assumption; exceeding it means a request is stuck, and
    /// failing loudly with diagnostics beats deadlocking the whole assembly silently.
    /// </summary>
    private static readonly TimeSpan BuildBudget = TimeSpan.FromSeconds(60);

    /// <summary>Gateway requests currently inside the server pipeline; diagnostics only.</summary>
    private static int _requestsInFlight;

    internal static int RequestsInFlight => Volatile.Read(ref _requestsInFlight);

    /// <summary>
    /// Runs <paramref name="build"/> with every request permit held, i.e. with no gateway request
    /// in flight anywhere in the assembly.
    /// </summary>
    internal static T Build<T>(Func<T> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        lock (BuildMutex)
        {
            AcquireAllPermits();

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
    /// Takes all <see cref="Capacity"/> permits within <see cref="BuildBudget"/>, releasing whatever
    /// it already took if the budget runs out. The failure carries the condition, elapsed time,
    /// attempt count and the last observation (permits taken, requests still in flight) so a stuck
    /// request is diagnosable instead of hanging the assembly.
    /// </summary>
    private static void AcquireAllPermits()
    {
        var started = Stopwatch.GetTimestamp();
        var acquired = 0;
        var attempts = 0;

        while (acquired < Capacity)
        {
            var remaining = BuildBudget - Stopwatch.GetElapsedTime(started);
            attempts++;

            if (remaining > TimeSpan.Zero && Permits.Wait(remaining))
            {
                acquired++;
                continue;
            }

            if (acquired > 0)
            {
                Permits.Release(acquired);
            }

            throw new TimeoutException(
                $"Gateway test host build could not reach the condition 'no gateway request in flight' "
                + $"({Capacity} permits held) after {Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s "
                + $"across {attempts} wait attempt(s). Last observation: {acquired} of {Capacity} permit(s) "
                + $"acquired, {RequestsInFlight} request(s) still inside the server pipeline. A gateway "
                + "request is stuck; the acquired permits were released so other requests can proceed.");
        }
    }

    /// <summary>
    /// Wraps the whole server pipeline of every host this assembly builds, so the permit covers all
    /// server-side work of a request — including writing the response body — and not merely the part
    /// that finishes before the response headers are flushed.
    /// </summary>
    internal sealed class RequestPermitStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(HoldPermitAsync);
                next(app);
            };
    }

    private static async Task HoldPermitAsync(HttpContext context, RequestDelegate next)
    {
        // Deliberately not cancellable: a request that is aborted mid-flight still has to take the
        // permit before any gateway code runs, otherwise the exclusion has a hole.
        await Permits.WaitAsync();
        Interlocked.Increment(ref _requestsInFlight);
        try
        {
            using (BusinessGatewayTestHost.TrackRequest(context))
            {
                await next(context);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _requestsInFlight);
            Permits.Release();
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
