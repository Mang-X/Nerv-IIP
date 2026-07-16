using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Seed;

/// <summary>
/// Maintenance 基础数据 seed（仿 <c>QualitySeedService</c> / <c>MasterDataSeedService</c>）：为全新
/// 环境补齐**点检保养计划**——PDA 点检页（equipment/inspect）先选保养计划再录测量值，计划目录
/// 为空会导致无计划可选、点检链路（含拍照/超差）无法端到端走通（L2 真栈走查前置）。
/// 幂等且**只补缺失项**：按 org/env + planCode 已存在（含被租户维护过的）一律跳过，保留租户事实——
/// 预置计划被操作员修改后重复 seed 不得覆写；与既有 seed 服务一致走 <c>SaveChangesAsync</c>（seed
/// 只需计划落库供 list 查询读取，不经命令管道、不派发领域事件）。
/// </summary>
public sealed class MaintenanceSeedService(ApplicationDbContext dbContext)
{
    private sealed record PlanSeed(string DeviceAssetId, string PlanCode, string Interval);

    // 覆盖日检 / 周检 / 月检三档周期，各绑定不同设备资产，贴合真实点检基线。Interval 为 ISO 日周期。
    private static readonly PlanSeed[] Plans =
    [
        new("DEV-CNC-01", "PM-INSP-DAILY-01", "P1D"),
        new("DEV-PUMP-02", "PM-INSP-WEEKLY-02", "P7D"),
        new("DEV-COMP-03", "PM-INSP-MONTHLY-03", "P30D"),
    ];

    // seed 计划起始日固定（确定性），不随运行时刻漂移；点检不依赖到期日，仅需可选计划存在。
    private static readonly DateOnly PlanStartsOn = new(2026, 7, 1);
    private const string PlanOwner = "maintenance";

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var existingCodes = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.PlanCode)
            .ToListAsync(cancellationToken);
        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in Plans)
        {
            // 只补缺失项：已存在（包括被租户维护过的）一律不动，保留租户事实。
            if (existing.Contains(seed.PlanCode))
            {
                continue;
            }

            dbContext.MaintenancePlans.Add(MaintenancePlan.Create(
                organizationId,
                environmentId,
                seed.DeviceAssetId,
                seed.PlanCode,
                seed.Interval,
                PlanStartsOn,
                PlanOwner));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
