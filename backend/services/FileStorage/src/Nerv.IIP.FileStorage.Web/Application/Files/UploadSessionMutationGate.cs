using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public enum UploadSessionMutationResult
{
    Mutated,
    NotFound,
    NotOpen
}

public interface IUploadSessionMutationGate
{
    Task<UploadSessionMutationResult> ExecutePatchMutationAsync(
        string uploadSessionId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken);
}

public sealed class UploadSessionMutationGate(
    IServiceScopeFactory scopeFactory,
    UploadSessionGateRegistry registry) : IUploadSessionMutationGate
{
    public async Task<UploadSessionMutationResult> ExecutePatchMutationAsync(
        string uploadSessionId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadSessionId);
        ArgumentNullException.ThrowIfNull(mutation);

        await using var gate = await registry.EnterPatchCommitAsync(uploadSessionId, cancellationToken);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await dbContext.UploadSessions
            .AsNoTracking()
            .Where(x => x.UploadSessionId == uploadSessionId)
            .Select(x => new { x.State, x.LegacyCompleted })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return UploadSessionMutationResult.NotFound;
        }

        if (session.LegacyCompleted
            || !string.Equals(session.State, UploadSessionState.Open, StringComparison.Ordinal))
        {
            return UploadSessionMutationResult.NotOpen;
        }

        await mutation(cancellationToken);
        return UploadSessionMutationResult.Mutated;
    }
}

public sealed class UploadSessionGateRegistry
{
    private readonly ConcurrentDictionary<string, GateEntry> gates = new(StringComparer.Ordinal);

    public ValueTask<IAsyncDisposable> EnterPatchCommitAsync(string uploadSessionId, CancellationToken cancellationToken) =>
        EnterAsync($"patch:{uploadSessionId}", cancellationToken);

    public ValueTask<IAsyncDisposable> EnterCommitExecutionAsync(string uploadSessionId, CancellationToken cancellationToken) =>
        EnterAsync($"commit:{uploadSessionId}", cancellationToken);

    private async ValueTask<IAsyncDisposable> EnterAsync(string key, CancellationToken cancellationToken)
    {
        GateEntry entry;
        while (true)
        {
            entry = gates.GetOrAdd(key, static _ => new GateEntry());
            lock (entry)
            {
                if (entry.Retired)
                {
                    continue;
                }

                entry.Users++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new GateLease(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void ReleaseReference(string key, GateEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (entry)
        {
            entry.Users--;
            if (entry.Users == 0)
            {
                entry.Retired = true;
                gates.TryRemove(new KeyValuePair<string, GateEntry>(key, entry));
            }
        }
    }

    private sealed class GateEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
        public bool Retired { get; set; }
    }

    private sealed class GateLease(UploadSessionGateRegistry owner, string key, GateEntry entry) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.ReleaseReference(key, entry, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
