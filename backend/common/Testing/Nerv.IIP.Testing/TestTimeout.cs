namespace Nerv.IIP.Testing;

public sealed class TestTimeoutException : TimeoutException
{
    public TestTimeoutException(string operation, TimeSpan elapsed)
        : base($"Operation '{operation}' timed out after {elapsed}.")
    {
        Operation = operation;
        Elapsed = elapsed;
    }

    public string Operation { get; }

    public TimeSpan Elapsed { get; }
}

public static class TestTimeout
{
    public static async ValueTask<T> RunAsync<T>(
        string operation,
        Func<CancellationToken, ValueTask<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null,
        IReadOnlyCollection<string?>? sensitiveValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        var startedAt = effectiveTimeProvider.GetTimestamp();
        using var timeoutSource = new CancellationTokenSource(timeout, effectiveTimeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            return await action(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            throw CreateException(
                operation,
                effectiveTimeProvider.GetElapsedTime(startedAt),
                sensitiveValues);
        }
    }

    public static ValueTask RunAsync(
        string operation,
        Func<CancellationToken, ValueTask> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null,
        IReadOnlyCollection<string?>? sensitiveValues = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Discard(RunAsync<object?>(
            operation,
            async token =>
            {
                await action(token).ConfigureAwait(false);
                return null;
            },
            timeout,
            cancellationToken,
            timeProvider,
            sensitiveValues));

        static async ValueTask Discard(ValueTask<object?> pending) => await pending.ConfigureAwait(false);
    }

    private static TestTimeoutException CreateException(
        string operation,
        TimeSpan elapsed,
        IReadOnlyCollection<string?>? sensitiveValues) =>
        new(TestDiagnostic.Sanitize(operation, sensitiveValues), elapsed);
}
