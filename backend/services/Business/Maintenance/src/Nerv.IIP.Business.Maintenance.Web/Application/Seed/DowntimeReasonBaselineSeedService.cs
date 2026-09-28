using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Category = Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate.DowntimeReasonVocabulary.ReasonCategories;
using Loss = Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate.DowntimeReasonVocabulary.LossCategories;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Seed;

/// <summary>
/// 停机原因目录的产品基线（#3855）：新环境里目录为空时，报修登记设备占用、完工登记停机原因、
/// MES 停机登记都走不通。这里预置一套按 TPM 六大损失 / OEE 损失口径分类的标准原因。
/// <list type="bullet">
/// <item>按 organization/environment + reasonCode 幂等只补缺；租户改过描述、分类或删掉重建的原因一律保留。</item>
/// <item>码与《工厂世界观设定集》演示种子（<see cref="WorldHistoryDeviceSpec.DowntimeReasons"/>）同名的 8 条沿用同一码，
///   两边都按码补缺，所以不会出现重复码；但同码内容并不完全一致——MECH / ELEC / PROC 的描述、
///   SETUP / MATERIAL / QUALITY 的分类与演示种子不同。基线先于演示种子运行，同码以基线为准；
///   两边的损失类别 8 条一致，OEE 口径不受影响。</item>
/// <item>分类与损失类别引用 <see cref="DowntimeReasonVocabulary"/>。分类依据：设备故障 / 换型调整属可用率损失，小停机空转属性能损失（TPM 六大损失前三类）；
///   工艺、质量、缺料、缺人、公用工程中断按国内 OEE 实践同样计入可用率损失；
///   计划保养、无生产计划是计划停机，不计入 OEE 损失。</item>
/// </list>
/// </summary>
public sealed class DowntimeReasonBaselineSeedService(ApplicationDbContext dbContext)
{
    public sealed record BaselineReason(string Code, string Description, string ReasonCategory, string LossCategory);

    public static readonly IReadOnlyList<BaselineReason> Reasons =
    [
        new("DT-MECH", "机械故障", Category.Breakdown, Loss.Availability),
        new("DT-ELEC", "电气故障", Category.Breakdown, Loss.Availability),
        new("DT-TOOL", "刀具/工装异常", Category.Breakdown, Loss.Availability),
        new("DT-SETUP", "换型调整", Category.Setup, Loss.Availability),
        new("DT-MINOR", "小停机/空转", Category.MinorStop, Loss.Performance),
        new("DT-PROC", "工艺参数异常", Category.Process, Loss.Availability),
        new("DT-QUALITY", "质量停机", Category.Quality, Loss.Availability),
        new("DT-MATERIAL", "缺料待工", Category.Material, Loss.Availability),
        new("DT-LABOR", "缺人待岗", Category.Labor, Loss.Availability),
        new("DT-UTILITY", "停水停电停气", Category.External, Loss.Availability),
        new("DT-PM", "计划保养", Category.Planned, Loss.Planned),
        new("DT-NOPLAN", "无生产计划", Category.Planned, Loss.Planned),
    ];

    public async Task<int> SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var codes = Reasons.Select(x => x.Code).ToArray();
        var existing = (await dbContext.DowntimeReasons
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && codes.Contains(x.ReasonCode))
                .Select(x => x.ReasonCode)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var reason in Reasons.Where(x => !existing.Contains(x.Code)))
        {
            dbContext.DowntimeReasons.Add(DowntimeReason.Create(
                organizationId,
                environmentId,
                reason.Code,
                reason.Description,
                reason.ReasonCategory,
                reason.LossCategory));
            written++;
        }

        if (written > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return written;
    }
}
