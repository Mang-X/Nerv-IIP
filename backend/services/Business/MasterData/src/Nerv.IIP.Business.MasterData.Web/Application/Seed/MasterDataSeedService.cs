using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Contracts.Coding;
using System.Text.Json;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public sealed class MasterDataSeedService(ApplicationDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // 计量单位表原先只有重量/计数/体积/时间四个量纲，质检特性要的力（N）、长度（mm）一个都没有——
    // 检验方案自己写着「1080–1320 N」，主数据里却查不到 N（#1396 / 走查 #80）。这里补齐质检常用量纲。
    // 逐码 upsert，新增码对既有库是纯增量且幂等，不需要重建库；下次服务启动播种时生效。
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

    private static readonly ShiftSeed[] Shifts =
    [
        new("DAY", "早班", new TimeOnly(8, 0), new TimeOnly(20, 0), 720),
        new("NIGHT", "晚班", new TimeOnly(20, 0), new TimeOnly(8, 0), 720)
    ];

    private static readonly DepartmentSeed[] Departments =
    [
        new("DEPT-PROD", "生产部", null),
        new("DEPT-QA", "质量部", null),
        new("DEPT-EQ", "设备部", null),
        new("DEPT-WH", "仓储部", null),
        new("DEPT-PLAN", "计划部", null)
    ];

    private static readonly ProductCategorySeed[] ProductCategories =
    [
        new("PCAT-SHOCK", "减振器总成", null, "面向整车厂交付的减振器成品分类"),
        new("PCAT-SHOCK-FR", "前减振器", "PCAT-SHOCK", "前悬架减振器总成"),
        new("PCAT-SHOCK-RR", "后减振器", "PCAT-SHOCK", "后悬架减振器总成"),
        new("PCAT-PART", "零部件", null, "减振器自制与外购零部件分类"),
        new("PCAT-PART-ROD", "活塞杆类", "PCAT-PART", "活塞杆棒料及其精加工件"),
        new("PCAT-PART-SEAL", "密封件类", "PCAT-PART", "油封、导向器等密封类零件")
    ];

    private static readonly SkillSeed[] Skills =
    [
        new("cnc-operation", "CNC 操作", "设备操作", true, 24, "数控加工中心上下料、程序调用与首件确认"),
        new("assembly", "减振器装配", "装配作业", false, null, "减振器总成装配线标准作业与扭矩控制"),
        new("inspection", "质量检验", "质量管理", true, 12, "首件、巡检与成品检验，含量具使用"),
        new("welding", "焊接", "特种作业", true, 36, "储油缸筒焊接，需持特种作业操作证"),
        new("equipment-maintenance", "设备维护", "设备管理", false, null, "设备点检保养与一般故障处理"),
        new("forklift", "叉车驾驶", "物流仓储", true, 48, "厂内叉车驾驶与物料转运，需持证上岗")
    ];

    /// <summary>
    /// 基线班组成员对应的员工档案。工号用 <c>EMP-9xx</c> 段：既避开设定集 L0 的 <c>EMP-001..058</c>，
    /// 也避开编码引擎 <c>worker</c> 规则发放的四位流水 <c>EMP-0001</c>，三者永不撞号。
    /// </summary>
    private static readonly WorkerSeed[] Workers =
    [
        new("EMP-901", "陈志强", "user-op-001", "DEPT-PROD", "装配班组长"),
        new("EMP-902", "李海涛", "user-op-002", "DEPT-PROD", "装配操作工"),
        new("EMP-903", "王建军", "user-op-003", "DEPT-PROD", "装配班组长"),
        new("EMP-904", "赵鹏", "user-op-004", "DEPT-PROD", "装配操作工"),
        new("EMP-905", "孙敏", "user-qc-001", "DEPT-QA", "质量检验员"),
        new("EMP-906", "周立新", "user-eq-001", "DEPT-EQ", "维修技师")
    ];

    private static readonly TeamSeed[] Teams =
    [
        new("TEAM-ASSY-A", "装配一线早班组", "DEPT-PROD", "DAY"),
        new("TEAM-ASSY-B", "装配一线晚班组", "DEPT-PROD", "NIGHT")
    ];

    private static readonly TeamMemberSeed[] TeamMembers =
    [
        new("TEAM-ASSY-A", "user-op-001", true),
        new("TEAM-ASSY-A", "user-op-002", false),
        new("TEAM-ASSY-A", "user-qc-001", false),
        new("TEAM-ASSY-B", "user-op-003", true),
        new("TEAM-ASSY-B", "user-op-004", false),
        new("TEAM-ASSY-B", "user-eq-001", false)
    ];

    private static readonly PersonnelSkillSeed[] PersonnelSkills =
    [
        new("user-op-001", "assembly", "senior"),
        new("user-op-001", "cnc-operation", "intermediate"),
        new("user-op-001", "inspection", "junior"),
        new("user-op-002", "assembly", "intermediate"),
        new("user-op-002", "equipment-maintenance", "junior"),
        new("user-qc-001", "inspection", "senior"),
        new("user-qc-001", "assembly", "junior"),
        new("user-op-003", "cnc-operation", "senior"),
        new("user-op-003", "welding", "intermediate"),
        new("user-op-004", "assembly", "intermediate"),
        new("user-op-004", "forklift", "junior"),
        new("user-eq-001", "equipment-maintenance", "expert"),
        new("user-eq-001", "cnc-operation", "intermediate")
    ];

    private static readonly DateOnly PersonnelSkillEffectiveFrom = new(2026, 1, 1);
    private static readonly DateOnly PersonnelSkillEffectiveTo = new(2030, 12, 31);
    private static readonly DateOnly TeamMemberEffectiveFrom = new(2026, 1, 1);

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        foreach (var rule in StandardCodeRules.All)
        {
            var segmentsJson = JsonSerializer.Serialize(rule.Segments, JsonOptions);
            var existing = await dbContext.CodeRules.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.RuleKey == rule.RuleKey,
                cancellationToken);
            if (existing is null)
            {
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
            }
            else
            {
                existing.ReplaceDefinition(
                    rule.DisplayName,
                    rule.AppliesTo,
                    (int)rule.Scope,
                    segmentsJson,
                    rule.IsActive,
                    rule.Version);
            }

            if (!await dbContext.CodeRuleVersions.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.RuleKey == rule.RuleKey &&
                    x.Version == rule.Version,
                    cancellationToken))
            {
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
        }

        foreach (var item in MasterDataDictionaryRules.StandardReferenceData)
        {
            var existing = await dbContext.ReferenceDataCodes.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.CodeSet == item.CodeSet &&
                x.Code == item.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create(
                    organizationId,
                    environmentId,
                    item.CodeSet,
                    item.Code,
                    item.Name));
            }
            else if (!existing.Disabled && !string.Equals(existing.Name, item.Name, StringComparison.Ordinal))
            {
                existing.Update(item.Name);
            }
        }

        var obsoleteCodeSets = MasterDataDictionaryRules.ObsoleteSeedCodes.Keys.ToArray();
        var obsoleteReferenceData = await dbContext.ReferenceDataCodes
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                obsoleteCodeSets.Contains(x.CodeSet) &&
                !x.Disabled)
            .ToArrayAsync(cancellationToken);
        foreach (var item in obsoleteReferenceData.Where(item =>
            MasterDataDictionaryRules.ObsoleteSeedCodes[item.CodeSet].Contains(item.Code)))
        {
            item.Disable("按主数据字典规则种子停用");
        }

        foreach (var item in Units)
        {
            var existing = await dbContext.UnitsOfMeasure.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == item.Code,
                cancellationToken);
            if (existing is null)
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
            else if (!existing.Disabled &&
                (!string.Equals(existing.Name, item.Name, StringComparison.Ordinal) ||
                 !string.Equals(existing.DimensionType, item.DimensionType, StringComparison.Ordinal) ||
                 existing.Precision != item.Precision ||
                 !string.Equals(existing.RoundingMode, item.RoundingMode, StringComparison.Ordinal)))
            {
                existing.Update(item.Name, item.DimensionType, item.Precision, item.RoundingMode);
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

        await SeedOrganizationAsync(organizationId, environmentId, cancellationToken);
        await SeedProductCategoriesAsync(organizationId, environmentId, cancellationToken);
        await SeedSkillsAsync(organizationId, environmentId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // 组织与班组：仅在缺失时补齐，已存在的租户数据一律保留（不覆盖、不改名）。
    private async Task SeedOrganizationAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
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

        foreach (var item in Workers)
        {
            if (!await dbContext.Workers.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.UserId == item.UserId,
                    cancellationToken))
            {
                dbContext.Workers.Add(Worker.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.UserId,
                    item.DepartmentCode,
                    item.JobTitle,
                    Worker.StatusActive,
                    null));
            }
        }

        foreach (var item in Teams)
        {
            if (!await dbContext.Teams.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.Teams.Add(Team.Create(organizationId, environmentId, item.Code, item.Name, item.DepartmentCode, item.ShiftCode));
            }
        }

        foreach (var item in TeamMembers)
        {
            if (!await dbContext.TeamMembers.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.TeamCode == item.TeamCode &&
                    x.UserId == item.UserId,
                    cancellationToken))
            {
                dbContext.TeamMembers.Add(TeamMember.Assign(
                    organizationId,
                    environmentId,
                    item.TeamCode,
                    item.UserId,
                    item.IsLeader,
                    TeamMemberEffectiveFrom,
                    null));
            }
        }

        foreach (var item in PersonnelSkills)
        {
            if (!await dbContext.PersonnelSkills.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.UserId == item.UserId &&
                    x.SkillCode == item.SkillCode,
                    cancellationToken))
            {
                dbContext.PersonnelSkills.Add(PersonnelSkill.Assign(
                    organizationId,
                    environmentId,
                    item.UserId,
                    item.SkillCode,
                    item.Level,
                    PersonnelSkillEffectiveFrom,
                    PersonnelSkillEffectiveTo));
            }
        }
    }

    private async Task SeedProductCategoriesAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var item in ProductCategories)
        {
            if (!await dbContext.ProductCategories.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.CategoryCode == item.Code,
                    cancellationToken))
            {
                dbContext.ProductCategories.Add(ProductCategory.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.ParentCode,
                    item.Description));
            }
        }
    }

    private async Task SeedSkillsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var item in Skills)
        {
            if (!await dbContext.Skills.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.SkillCode == item.Code,
                    cancellationToken))
            {
                dbContext.Skills.Add(Skill.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.GroupName,
                    item.RequiresCertification,
                    item.ValidityMonths,
                    item.Description));
            }
        }
    }

    private sealed record UomSeed(string Code, string Name, string DimensionType, int Precision, string RoundingMode);

    private sealed record UomConversionSeed(string FromUomCode, string ToUomCode, decimal Factor, int Precision);

    private sealed record ShiftSeed(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);

    private sealed record DepartmentSeed(string Code, string Name, string? ParentCode);

    private sealed record ProductCategorySeed(string Code, string Name, string? ParentCode, string Description);

    private sealed record SkillSeed(string Code, string Name, string GroupName, bool RequiresCertification, int? ValidityMonths, string Description);

    private sealed record WorkerSeed(string Code, string Name, string UserId, string DepartmentCode, string JobTitle);

    private sealed record TeamSeed(string Code, string Name, string DepartmentCode, string ShiftCode);

    private sealed record TeamMemberSeed(string TeamCode, string UserId, bool IsLeader);

    private sealed record PersonnelSkillSeed(string UserId, string SkillCode, string Level);
}
