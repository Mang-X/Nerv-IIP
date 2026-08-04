using System.Diagnostics;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// One test's isolated slot on a shared host: the downstream fakes it registered, its own
/// downstream-health recorder, and a count of the requests currently using them.
/// </summary>
internal sealed class BusinessGatewayTestScope(IReadOnlyDictionary<Type, object> overrides)
{
    /// <summary>
    /// Upper bound on how long <see cref="DrainAsync"/> waits for a lease's own requests to leave
    /// the server pipeline. A lease is only disposed after its test awaited its responses, so this
    /// is slack for the pipeline tail, not a synchronisation primitive with a timing assumption.
    /// </summary>
    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(30);

    private int _requestsInFlight;

    /// <summary>
    /// Completed by the request that takes the in-flight count back to zero. Created on demand by
    /// the first <see cref="DrainAsync"/>, so a scope nobody drains costs nothing.
    /// </summary>
    private TaskCompletionSource? _drained;

    public string Id { get; } = Guid.CreateVersion7().ToString("n");

    public IReadOnlyDictionary<Type, object> Overrides { get; } = overrides;

    public BusinessGatewayDownstreamHealthState HealthState { get; } = new();

    /// <summary>Marks a request as using this scope until the returned handle is disposed.</summary>
    public IDisposable Enter()
    {
        Interlocked.Increment(ref _requestsInFlight);
        return new Registration(this);
    }

    /// <summary>
    /// Waits until no request is inside the server pipeline under this scope, so unregistering it
    /// cannot make a still-running request fail to resolve its own fakes.
    /// </summary>
    public async ValueTask DrainAsync()
    {
        if (Volatile.Read(ref _requestsInFlight) == 0)
        {
            return;
        }

        Interlocked.CompareExchange(
            ref _drained,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            null);
        var drained = Volatile.Read(ref _drained)!;

        // The last request may have left between the check above and publishing the source, in
        // which case nobody is left to complete it.
        if (Volatile.Read(ref _requestsInFlight) == 0)
        {
            drained.TrySetResult();
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await drained.Task.WaitAsync(DrainBudget);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"Gateway test scope '{Id}' still had {Volatile.Read(ref _requestsInFlight)} request(s) in flight "
                + $"after {Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s. The owning test released its lease "
                + "without awaiting its own responses.",
                exception);
        }
    }

    private void Leave()
    {
        if (Interlocked.Decrement(ref _requestsInFlight) == 0)
        {
            Volatile.Read(ref _drained)?.TrySetResult();
        }
    }

    private sealed class Registration(BusinessGatewayTestScope scope) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                scope.Leave();
            }
        }
    }
}
