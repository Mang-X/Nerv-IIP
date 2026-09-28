using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;

namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 的**生产准备底座**块：SKU 停用投影。
///
/// 这是主数据投影（不是历史事实），与工单链无先后依赖，可独立于
/// <see cref="WorldHistorySeedService"/> 运行。形状见 <see cref="WorldHistoryFoundationSpec"/>。
/// 幂等自然键：(org, env, SKU 编码)——与表的唯一索引同构。
/// 设备 ↔ 工作中心归属由 MasterData 拥有，MES 不再写本地映射（#3878）。
/// </summary>
public sealed class WorldHistoryFoundationSeedService(ApplicationDbContext dbContext)
{
    public async Task<WorldHistoryFoundationSeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var disabledSkusWritten = await SeedDisabledSkusAsync(organizationId, environmentId, cancellationToken);

        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateFoundationAsync(organizationId, environmentId, cancellationToken);

        return new WorldHistoryFoundationSeedReport(disabledSkusWritten, validation);
    }

    private async Task<int> SeedDisabledSkusAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var disabled = WorldHistoryFoundationSpec.DisabledSkus;
        var skuCodes = disabled.Select(x => x.SkuCode).ToArray();
        var existing = (await dbContext.MesSkuAvailabilities
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    skuCodes.Contains(x.SkuCode))
                .Select(x => x.SkuCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        for (var index = 0; index < disabled.Count; index++)
        {
            var sku = disabled[index];
            if (existing.Contains(sku.SkuCode))
            {
                continue;
            }

            dbContext.MesSkuAvailabilities.Add(MesSkuAvailability.CreateDisabled(
                organizationId,
                environmentId,
                sku.SkuCode,
                WorldHistoryFoundationSpec.DisabledAtUtc(index),
                sku.DisabledReason,
                WorldHistoryFoundationSpec.DisabledSourceEventId(sku.SkuCode)));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        return added;
    }
}

/// <summary>一次 L1 生产准备底座块生成的产出摘要。</summary>
public sealed record WorldHistoryFoundationSeedReport(
    int DisabledSkusWritten,
    WorldHistoryFoundationValidationReport Validation);
