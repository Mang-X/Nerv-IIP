using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

/// <summary>
/// Quality 基础目录 seed（仿 MasterDataSeedService）：为全新环境补齐**质量原因码目录**——
/// 检验执行（PDA/console）里计数特性判不合格时的原因码 Picker 依赖它，目录为空会导致无码可选。
/// 幂等：按 org/env + reasonCode 存在即更新名称/分组/严重度/默认处置，不重复插入。
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
        foreach (var seed in Reasons)
        {
            var existing = await dbContext.QualityReasons.SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                     && x.EnvironmentId == environmentId
                     && x.ReasonCode == seed.ReasonCode,
                cancellationToken);
            if (existing is null)
            {
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
            else
            {
                existing.Update(seed.ReasonName, seed.GroupName, seed.Severity, seed.DefaultDisposition);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
