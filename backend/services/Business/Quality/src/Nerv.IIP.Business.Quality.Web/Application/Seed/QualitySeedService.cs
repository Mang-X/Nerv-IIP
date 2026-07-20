using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

/// <summary>
/// Quality 基础目录 seed（仿 MasterDataSeedService）：为全新环境补齐**质量原因码目录**——
/// 检验执行（PDA/console）里计数特性判不合格时的原因码 Picker 依赖它，目录为空会导致无码可选。
/// 幂等且**只补缺失项**：按 org/env + reasonCode 已存在（含被归档/被租户改名）一律跳过，
/// 保留租户事实——预置码被操作员归档或修改后，重复 seed 不得复活/覆写，更不能因
/// `Update()` 的 EnsureEnabled 抛错导致服务无法启动。
/// </summary>
public sealed class QualitySeedService(ApplicationDbContext dbContext)
{
    private sealed record ReasonSeed(
        string ReasonCode,
        string ReasonName,
        string GroupName,
        string Severity,
        string? DefaultDisposition);

    // 常见不良类型基线（覆盖外观/尺寸/功能/材料/包装/标识），与 NCR 处置口径
    // （rework/scrap/return-to-supplier/conditional-release）对齐。
    private static readonly ReasonSeed[] Reasons =
    [
        new("RSN-APPEARANCE", "外观缺陷", "外观", "minor", "rework"),
        new("RSN-DIMENSION", "尺寸超差", "尺寸", "major", "rework"),
        new("RSN-FUNC-FAIL", "功能失效", "功能", "critical", "scrap"),
        new("RSN-MATERIAL", "材料不符", "材料", "major", "return-to-supplier"),
        new("RSN-PACKAGING", "包装破损", "包装", "minor", "conditional-release"),
        new("RSN-LABELING", "标识错误", "标识", "minor", "rework"),
        new("RSN-CONTAMINATION", "污染异物", "清洁度", "critical", "scrap"),
    ];

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var existingCodes = await dbContext.QualityReasons
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.ReasonCode)
            .ToListAsync(cancellationToken);
        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in Reasons)
        {
            // 只补缺失项：已存在（包括被归档或被租户维护过的）一律不动，保留租户事实。
            if (existing.Contains(seed.ReasonCode))
            {
                continue;
            }

            dbContext.QualityReasons.Add(QualityReason.Create(
                organizationId,
                environmentId,
                seed.ReasonCode,
                seed.ReasonName,
                seed.GroupName,
                seed.Severity,
                seed.DefaultDisposition,
                enabled: true));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
