namespace Nerv.IIP.Business.Performance.Tests;

internal sealed class ProcessMemorySampler : IAsyncDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task samplingTask;
    private readonly long baselineWorkingSetBytes;
    private long peakWorkingSetBytes;
    private long peakManagedHeapBytes;
    private bool stopped;

    private ProcessMemorySampler()
    {
        process = Process.GetCurrentProcess();
        process.Refresh();
        baselineWorkingSetBytes = process.WorkingSet64;
        peakWorkingSetBytes = baselineWorkingSetBytes;
        peakManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        samplingTask = SampleAsync(cancellation.Token);
    }

    public long PeakWorkingSetBytes => Interlocked.Read(ref peakWorkingSetBytes);
    public long PeakManagedHeapBytes => Interlocked.Read(ref peakManagedHeapBytes);
    public long WorkingSetIncreaseBytes => Math.Max(0, PeakWorkingSetBytes - baselineWorkingSetBytes);

    public static ProcessMemorySampler Start() => new();

    public async Task StopAsync()
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        Observe();
        await cancellation.CancelAsync();
        await samplingTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        cancellation.Dispose();
        process.Dispose();
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Observe();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Observe()
    {
        process.Refresh();
        UpdateMaximum(ref peakWorkingSetBytes, process.WorkingSet64);
        UpdateMaximum(ref peakManagedHeapBytes, GC.GetTotalMemory(forceFullCollection: false));
    }

    private static void UpdateMaximum(ref long target, long candidate)
    {
        var current = Interlocked.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
