using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Scheduling.Infrastructure;

/// <summary>
/// AssetUnavailable inbox 双身份 claim 的串行化边界：同一 consumer 下，同一事件实例（EventId）与同一业务事实
/// （IdempotencyKey）的并发 claim 必须在同一把锁后面排队，claim 才能"先查后写"而不产生两条失效记录。
/// provider 专有实现留在 Infrastructure；Application 只依赖本接口。
/// </summary>
public interface IAssetUnavailableInboxIdentityLock
{
    Task AcquireAsync(string consumerName, IIntegrationEventEnvelope integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// PostgreSQL 实现：在当前工作单元事务内按 EventId 与 IdempotencyKey 各取一把事务级 advisory lock（按键序数排序，
/// 避免两个竞争者以相反顺序取锁而互相等待）。锁随事务提交/回滚释放，因此 claim 必须已经处于事务内；
/// 非 PostgreSQL provider（单测 InMemory）没有跨连接的串行化手段，直接放行，由唯一索引兜底。
/// </summary>
public sealed class PostgreSqlAssetUnavailableInboxIdentityLock(ApplicationDbContext dbContext)
    : IAssetUnavailableInboxIdentityLock
{
    public async Task AcquireAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "AssetUnavailable inbox claim requires an active unit-of-work transaction.");
        }

        var lockKeys = new[]
        {
            $"scheduling-asset-unavailable:event:{consumerName}:{integrationEvent.EventId}",
            $"scheduling-asset-unavailable:business:{consumerName}:{integrationEvent.IdempotencyKey}"
        };
        foreach (var lockKey in lockKeys.Order(StringComparer.Ordinal))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }
    }
}
