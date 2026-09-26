using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    /// <summary>派工按「工作中心 → 车间 → 班组」收敛，演示工作中心必须挂在车间下才有候选人。</summary>
    private const string DemoWorkshopCode = "WS-DEMO";

    // 以下员工、班组、技能、产品分类与工厂自定义字典样例原属常规主数据种子，#3811 按「演示最小集」
    // 挪到这里：它们是虚构事实，不进产品基线。只补缺，已存在的租户事实不覆盖。
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
    /// 演示班组成员对应的员工档案。工号用 <c>EMP-9xx</c> 段：既避开设定集 L0 的 <c>EMP-001..058</c>，
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
        new("TEAM-ASSY-A", "装配一线白班组", "DEPT-PROD", "DAY"),
        new("TEAM-ASSY-B", "装配一线夜班组", "DEPT-PROD", "NIGHT")
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
        await SeedFactoryCustomReferenceDataAsync(organizationId, environmentId, cancellationToken);
        await SeedProductCategoriesAsync(organizationId, environmentId, cancellationToken);
        await SeedSkillsAsync(organizationId, environmentId, cancellationToken);
        await SeedPeopleAsync(organizationId, environmentId, cancellationToken);

        var site = await dbContext.Sites.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "SITE-001", cancellationToken);
        if (site is null)
        {
            dbContext.Sites.Add(Site.Create(organizationId, environmentId, "SITE-001", "一号工厂", "Asia/Shanghai"));
        }
        else if (site.Name != "一号工厂" || site.Timezone != "Asia/Shanghai" || site.Disabled)
        {
            throw Collision("SITE-001");
        }

        var line = await dbContext.ProductionLines.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "LINE-DEMO-01", cancellationToken);
        if (line is null)
        {
            dbContext.ProductionLines.Add(ProductionLine.Create(organizationId, environmentId, "LINE-DEMO-01", "减振器装配一线", "SITE-001"));
        }
        else if (line.Name != "减振器装配一线" || line.SiteCode != "SITE-001" || line.WorkshopCode is not null || line.Disabled)
        {
            throw Collision("LINE-DEMO-01");
        }

        var demoWorkshop = await dbContext.Workshops.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == DemoWorkshopCode, cancellationToken);
        if (demoWorkshop is null)
        {
            dbContext.Workshops.Add(Workshop.Create(organizationId, environmentId, DemoWorkshopCode, "演示车间", "SITE-001", null, null));
        }

        var workCenter = await dbContext.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "WC-CNC-DEMO", cancellationToken);
        if (workCenter is null)
        {
            dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                organizationId, environmentId, "WC-CNC-DEMO", "CNC 精加工中心", 480, "work-center",
                "SITE-001", "LINE-DEMO-01", DemoWorkshopCode, "STANDARD", "minute", true));
        }
        else if (workCenter.Name != "CNC 精加工中心" || workCenter.CapacityMinutesPerDay != 480 ||
                 workCenter.PlantCode != "SITE-001" || workCenter.LineCode != "LINE-DEMO-01" || workCenter.Disabled)
        {
            throw Collision("WC-CNC-DEMO");
        }
        else if (workCenter.WorkshopCode is null)
        {
            // 车间归属是本次新增的事实，旧环境补挂即可，不算冲突。
            workCenter.UpdateResource(
                workCenter.Name,
                workCenter.CapacityMinutesPerDay,
                workCenter.ResourceType,
                workCenter.PlantCode ?? "SITE-001",
                workCenter.LineCode ?? "LINE-DEMO-01",
                DemoWorkshopCode,
                workCenter.DefaultCalendarCode ?? "STANDARD",
                workCenter.CapacityUnit,
                workCenter.FiniteCapacity);
        }

        // 派工按「工作中心 → 车间 → 班组」收敛，所以演示班组挂在车间上；WC-CNC-DEMO 未挂车间时
        // 该链路查不到人，这里一并把演示工作中心归到 WS-DEMO。
        var cncTeam = await dbContext.Teams.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "TEAM-CNC-DEMO", cancellationToken);
        if (cncTeam is null)
        {
            dbContext.Teams.Add(Team.Create(organizationId, environmentId, "TEAM-CNC-DEMO", "CNC 精加工班组", "DEPT-PROD", "DAY", DemoWorkshopCode));
        }
        else if (cncTeam.WorkshopCode is null)
        {
            cncTeam.Update(cncTeam.Name, cncTeam.DepartmentCode, cncTeam.ShiftCode, DemoWorkshopCode);
        }

        foreach (var (userId, isLeader) in new[] { ("user-op-003", true), ("user-op-001", false) })
        {
            if (!await dbContext.TeamMembers.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.TeamCode == "TEAM-CNC-DEMO" &&
                    x.UserId == userId,
                    cancellationToken))
            {
                dbContext.TeamMembers.Add(TeamMember.Assign(
                    organizationId,
                    environmentId,
                    "TEAM-CNC-DEMO",
                    userId,
                    isLeader,
                    new DateOnly(2026, 1, 1),
                    null));
            }
        }

        await SeedSkuAsync(organizationId, environmentId, "SKU-DEMO-001", "汽车减振器总成", "finished-goods", cancellationToken);
        await SeedSkuAsync(organizationId, environmentId, "SKU-DEMO-RM-001", "活塞杆棒料", "raw-material", cancellationToken);

        // #1290：主力演示客户信用额度 2000 万（CNY），与其世界史订单体量（权重 8/46 摊 4413 单）匹配；只补缺失。
        const decimal demoCustomerCreditLimit = 20_000_000m;
        var customer = await dbContext.BusinessPartners.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "CUST-DEMO-001", cancellationToken);
        if (customer is null)
        {
            dbContext.BusinessPartners.Add(BusinessPartner.Create(
                organizationId, environmentId, "CUST-DEMO-001", "customer", "华东汽车零部件采购中心",
                ["customer"], taxId: null, creditLimit: demoCustomerCreditLimit, creditCurrencyCode: "CNY"));
        }
        else if (customer.Name != "华东汽车零部件采购中心" || customer.PartnerType != "customer" || customer.Disabled)
        {
            throw Collision("CUST-DEMO-001");
        }
        else if (customer.CreditLimit is null)
        {
            customer.UpdateCreditLimit(demoCustomerCreditLimit, "CNY");
        }

        var device = await dbContext.DeviceAssets.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "DEV-CNC-DEMO", cancellationToken);
        if (device is null)
        {
            dbContext.DeviceAssets.Add(DeviceAsset.RegisterCapability(
                organizationId, environmentId, "DEV-CNC-DEMO", "立式加工中心 VMC-850", "LINE-DEMO-01", "WC-CNC-DEMO",
                "cnc", "", "", null, null, "", "high", true, true, new Dictionary<string, string>()));
        }
        else if (device.Model != "立式加工中心 VMC-850" || device.LineCode != "LINE-DEMO-01" || device.WorkCenterCode != "WC-CNC-DEMO" ||
                 !device.Maintainable || !device.TelemetryEnabled || device.Disabled)
        {
            throw Collision("DEV-CNC-DEMO");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSkuAsync(string organizationId, string environmentId, string code, string name, string category, CancellationToken cancellationToken)
    {
        var sku = await dbContext.Skus.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code, cancellationToken);
        if (sku is null)
        {
            dbContext.Skus.Add(Sku.Create(organizationId, environmentId, code, name, "pcs", category));
        }
        else if (sku.Name != name || sku.Unit != "pcs" || sku.Category != category || sku.Disabled)
        {
            throw Collision(code);
        }
    }

    private async Task SeedFactoryCustomReferenceDataAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var item in MasterDataDictionaryRules.StandardReferenceData
            .Where(x => x.Kind == ReferenceDataCodeSetKind.FactoryCustom))
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
    }

    private async Task SeedPeopleAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
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

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo master-data fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");

    private sealed record ProductCategorySeed(string Code, string Name, string? ParentCode, string Description);

    private sealed record SkillSeed(string Code, string Name, string GroupName, bool RequiresCertification, int? ValidityMonths, string Description);

    private sealed record WorkerSeed(string Code, string Name, string UserId, string DepartmentCode, string JobTitle);

    private sealed record TeamSeed(string Code, string Name, string DepartmentCode, string ShiftCode);

    private sealed record TeamMemberSeed(string TeamCode, string UserId, bool IsLeader);

    private sealed record PersonnelSkillSeed(string UserId, string SkillCode, string Level);
}
