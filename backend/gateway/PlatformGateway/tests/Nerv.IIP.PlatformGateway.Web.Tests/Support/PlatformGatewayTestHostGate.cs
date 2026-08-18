using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

/// <summary>
/// Excludes PlatformGateway host construction from in-flight gateway requests while allowing
/// requests to execute in parallel.
/// </summary>
/// <remarks>
/// <c>Program.cs</c> configures FastEndpoints during host construction. FastEndpoints keeps that
/// configuration in process-wide state, so building one host while another host is serving a
/// request is unsafe. The request permit is held by the outermost server middleware so it covers
/// the complete response body, not only the client handler's response-header boundary.
/// </remarks>
internal static class PlatformGatewayTestHostGate
{
    private static readonly int Capacity = Math.Max(256, Environment.ProcessorCount * 32);
    private static readonly SemaphoreSlim Permits = new(Capacity, Capacity);
    private static readonly Lock BuildMutex = new();
    private static readonly TimeSpan BuildBudget = TimeSpan.FromSeconds(60);

    private static int _requestsInFlight;

    internal static int RequestsInFlight => Volatile.Read(ref _requestsInFlight);

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
                $"PlatformGateway test host build could not reach the condition 'no gateway request in flight' "
                + $"({Capacity} permits held) after {Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s "
                + $"across {attempts} wait attempt(s). Last observation: {acquired} of {Capacity} permit(s) "
                + $"acquired, {RequestsInFlight} request(s) still inside the server pipeline. A gateway "
                + "request is stuck; the acquired permits were released so other requests can proceed.");
        }
    }

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
        await Permits.WaitAsync();
        Interlocked.Increment(ref _requestsInFlight);
        try
        {
            await next(context);
        }
        finally
        {
            Interlocked.Decrement(ref _requestsInFlight);
            Permits.Release();
        }
    }
}
