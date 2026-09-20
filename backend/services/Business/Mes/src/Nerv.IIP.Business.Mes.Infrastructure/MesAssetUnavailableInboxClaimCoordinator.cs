using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Mes.Infrastructure;

/// <summary>
/// Maintenance AssetUnavailable（v1/v2 汇入同一 canonical 事实）的双身份收件箱 claim：
/// 只有在同一事务里同时赢得 <c>(ConsumerName, EventId)</c> 事件实例身份与 <c>(ConsumerName, IdempotencyKey)</c>
/// 业务事实身份的调用方才能继续副作用（#2964）。provider 专有的并发手段留在 Infrastructure，Application 只依赖本接口。
/// </summary>
public interface IMesAssetUnavailableInboxClaimCoordinator
{
    /// <returns>true = 赢得双身份并已把收件箱行加入当前 UoW；false = 任一身份已被处理，调用方不得执行副作用。</returns>
    Task<bool> TryClaimAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// PostgreSQL 上先在当前 UoW 事务内按序数顺序拿两把 <c>pg_advisory_xact_lock</c>（事件实例身份与业务事实身份各一把），
/// 把并发竞争者挡在 claim 这一行而不是唯一索引上；锁随事务提交/回滚释放，落败者随后看到已提交的收件箱行并返回 false。
/// 两条唯一索引仍是最后一道防线。非 PostgreSQL provider 只保留读-写检查，供 provider-light 用例使用；姿势同本目录其它 *Coordinator。
/// </summary>
public sealed class PostgreSqlMesAssetUnavailableInboxClaimCoordinator(ApplicationDbContext dbContext)
    : IMesAssetUnavailableInboxClaimCoordinator
{
    public async Task<bool> TryClaimAsync(
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationEvent.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationEvent.IdempotencyKey);

        if (dbContext.Database.IsNpgsql())
        {
            if (dbContext.Database.CurrentTransaction is null)
            {
                throw new InvalidOperationException(
                    "AssetUnavailable inbox claim requires an active unit-of-work transaction so the advisory locks are released with the claim.");
            }

            var lockKeys = new[]
            {
                $"mes-asset-unavailable:event:{consumerName}:{integrationEvent.EventId}",
                $"mes-asset-unavailable:business:{consumerName}:{integrationEvent.IdempotencyKey}",
            };
            foreach (var lockKey in lockKeys.Order(StringComparer.Ordinal))
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                    cancellationToken);
            }
        }

        var alreadyClaimed = dbContext.ProcessedIntegrationEvents.Local.Any(processed =>
            processed.ConsumerName == consumerName &&
            (processed.EventId == integrationEvent.EventId ||
             processed.IdempotencyKey == integrationEvent.IdempotencyKey));
        if (alreadyClaimed || await dbContext.ProcessedIntegrationEvents.AnyAsync(
                processed =>
                    processed.ConsumerName == consumerName &&
                    (processed.EventId == integrationEvent.EventId ||
                     processed.IdempotencyKey == integrationEvent.IdempotencyKey),
                cancellationToken))
        {
            return false;
        }

        dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent(
            consumerName,
            integrationEvent.EventId,
            integrationEvent.EventType,
            integrationEvent.EventVersion,
            integrationEvent.SourceService,
            integrationEvent.IdempotencyKey,
            DateTimeOffset.UtcNow));
        return true;
    }
}
