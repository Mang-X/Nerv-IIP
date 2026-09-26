using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Contracts.Coding;
using System.Text.Json;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

/// <summary>
/// MasterData 产品基线种子：编码规则、受控字典、计量单位与换算、班次、工作日历、部门这些
/// 基础功能必需的开箱数据（#3811）。默认随 Web 启动执行，只补缺：已存在的行（含租户改过、
/// 停用过的）一律不改、不停用。演示用的员工、班组、技能、产品分类与工厂自定义字典样例归
/// <see cref="LeaderDemoSeedService"/>。
/// </summary>
public sealed class MasterDataSeedService(ApplicationDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // 计量单位表原先只有重量/计数/体积/时间四个量纲，质检特性要的力（N）、长度（mm）一个都没有——
    // 检验方案自己写着「1080–1320 N」，主数据里却查不到 N（#1396 / 走查 #80）。这里补齐质检常用量纲。
    // 逐码只补缺，新增码对既有库是纯增量且幂等，不需要重建库；下次服务启动播种时生效。
    private static readonly UomSeed[] Units =
    [
        new("kg", "千克", "weight", 3, "half-up"),
        new("g", "克", "weight", 3, "half-up"),
        new("pcs", "件", "count", 0, "half-up"),
        new("l", "升", "volume", 3, "half-up"),
        new("min", "分钟", "time", 0, "half-up"),
        new("s", "秒", "time", 0, "half-up"),
        new("m", "米", "length", 3, "half-up"),
        new("mm", "毫米", "length", 3, "half-up"),
        new("N", "牛顿", "force", 3, "half-up"),
        new("Nm", "牛·米", "torque", 3, "half-up"),
        new("MPa", "兆帕", "pressure", 3, "half-up"),
        new("%", "百分比", "ratio", 2, "half-up")
    ];

    private static readonly DateOnly UomConversionEffectiveFrom = new(2026, 1, 1);

    // 同量纲内的换算关系：检验记录允许「录入单位 ≠ 方案单位」但必须有换算行兜底，
    // 否则领域层直接拒收（InspectionRecord 的单位一致性校验）。
    private static readonly UomConversionSeed[] UomConversions =
    [
        new("kg", "g", 1000m, 3),
        new("m", "mm", 1000m, 3),
        new("min", "s", 60m, 0)
    ];

    // 显示名的权威是 docs/product/master-data/design.md §5.3「种子：组织/班次/日历」：
    // DAY=白班(08:00-20:00)、NIGHT=夜班(20:00-08:00,跨天)。本数组曾写成「早班」「晚班」，
    // 其中「早班」还与设定集种子的 EARLY(08:00-16:00) 撞名（#3473）。
    // 已知偏离且有意不补：文档还列了 NORMAL=常白班(08:30-17:30)，本种子没有它。
    // 那是「少一个班次」的缺失，不是「屏上看到错的东西」的矛盾，不在 #3473 范围内。
    private static readonly ShiftSeed[] Shifts =
    [
        new("DAY", "白班", new TimeOnly(8, 0), new TimeOnly(20, 0), 720),
        new("NIGHT", "夜班", new TimeOnly(20, 0), new TimeOnly(8, 0), 720)
    ];

    private static readonly DepartmentSeed[] Departments =
    [
        new("DEPT-PROD", "生产部", null),
        new("DEPT-QA", "质量部", null),
        new("DEPT-EQ", "设备部", null),
        new("DEPT-WH", "仓储部", null),
        new("DEPT-PLAN", "计划部", null)
    ];

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        foreach (var rule in StandardCodeRules.All)
        {
            if (await dbContext.CodeRules.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.RuleKey == rule.RuleKey,
                    cancellationToken))
            {
                continue;
            }

            var segmentsJson = JsonSerializer.Serialize(rule.Segments, JsonOptions);
            dbContext.CodeRules.Add(CodeRule.Create(
                organizationId,
                environmentId,
                rule.RuleKey,
                rule.DisplayName,
                rule.AppliesTo,
                (int)rule.Scope,
                segmentsJson,
                rule.IsActive,
                rule.Version));
            dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
                organizationId,
                environmentId,
                rule.RuleKey,
                rule.DisplayName,
                rule.AppliesTo,
                (int)rule.Scope,
                segmentsJson,
                rule.IsActive,
                rule.Version,
                CodeRuleVersionStatus.Active,
                DateTimeOffset.UnixEpoch,
                "standard-seed",
                "标准编码规则种子",
                DateTimeOffset.UtcNow));
        }

        // 工厂自定义码集（skill/operation/quality-reason）的具体值是演示样例，归 LeaderDemo 种子。
        foreach (var item in MasterDataDictionaryRules.StandardReferenceData
            .Where(x => x.Kind != ReferenceDataCodeSetKind.FactoryCustom))
        {
            if (!await dbContext.ReferenceDataCodes.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.CodeSet == item.CodeSet &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create(
                    organizationId,
                    environmentId,
                    item.CodeSet,
                    item.Code,
                    item.Name));
            }
        }

        foreach (var item in Units)
        {
            if (!await dbContext.UnitsOfMeasure.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.UnitsOfMeasure.Add(UnitOfMeasure.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.DimensionType,
                    item.Precision,
                    item.RoundingMode));
            }
        }

        foreach (var item in UomConversions)
        {
            if (!await dbContext.UomConversions.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.FromUomCode == item.FromUomCode &&
                    x.ToUomCode == item.ToUomCode &&
                    x.EffectiveFrom == UomConversionEffectiveFrom,
                    cancellationToken))
            {
                dbContext.UomConversions.Add(UomConversion.Create(
                    organizationId,
                    environmentId,
                    item.FromUomCode,
                    item.ToUomCode,
                    item.Factor,
                    0m,
                    item.Precision,
                    "half-up",
                    UomConversionEffectiveFrom));
            }
        }

        foreach (var item in Shifts)
        {
            if (!await dbContext.Shifts.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.Shifts.Add(Shift.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.StartsAt,
                    item.EndsAt,
                    item.PaidMinutes));
            }
        }

        if (!await dbContext.WorkCalendars.AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == "STANDARD",
                cancellationToken))
        {
            var calendar = WorkCalendar.Create(organizationId, environmentId, "STANDARD", "标准工作日历");
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            {
                calendar.AddWorkingDay(day);
            }

            dbContext.WorkCalendars.Add(calendar);
        }

        foreach (var item in Departments)
        {
            if (!await dbContext.Departments.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.Departments.Add(Department.Create(organizationId, environmentId, item.Code, item.Name, item.ParentCode));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record UomSeed(string Code, string Name, string DimensionType, int Precision, string RoundingMode);

    private sealed record UomConversionSeed(string FromUomCode, string ToUomCode, decimal Factor, int Precision);

    private sealed record ShiftSeed(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);

    private sealed record DepartmentSeed(string Code, string Name, string? ParentCode);
}
