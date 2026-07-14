using MediatR;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Infrastructure;

public sealed class MaintenanceCommandLockBehavior<TRequest, TResponse>(
    IEnumerable<ICommandLock<TRequest>> commandLocks,
    IDistributedLock distributedLock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var lockProviders = commandLocks.ToArray();
        if (lockProviders.Length == 0)
        {
            return await next(cancellationToken);
        }

        var handles = new List<ILockSynchronizationHandler>();
        var acquiredKeys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var lockProvider in lockProviders)
            {
                var settings = await lockProvider.GetLockKeysAsync(request, cancellationToken);
                foreach (var key in EnumerateKeys(settings))
                {
                    if (!acquiredKeys.Add(key))
                    {
                        continue;
                    }

                    handles.Add(await distributedLock.AcquireAsync(key, settings.AcquireTimeout, cancellationToken));
                }
            }

            if (handles.Count == 0)
            {
                throw new InvalidOperationException($"Command lock configuration for {typeof(TRequest).Name} did not provide a lock key.");
            }

            var linkedTokens = new CancellationToken[handles.Count + 1];
            linkedTokens[0] = cancellationToken;
            for (var i = 0; i < handles.Count; i++)
            {
                linkedTokens[i + 1] = handles[i].HandleLostToken;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(linkedTokens);
            return await next(linkedCancellation.Token);
        }
        finally
        {
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                await handles[i].DisposeAsync();
            }
        }
    }

    private static IEnumerable<string> EnumerateKeys(CommandLockSettings settings)
    {
        if (settings.LockKeys is not null)
        {
            foreach (var key in settings.LockKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return key;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.LockKey))
        {
            yield return settings.LockKey;
        }
    }
}
