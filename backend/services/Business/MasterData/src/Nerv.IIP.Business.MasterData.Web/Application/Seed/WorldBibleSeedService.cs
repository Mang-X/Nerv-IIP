using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
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

/// <summary>
/// 《工厂世界观设定集》L0 主数据（§1–§6）的 MasterData 侧种子：组织/人员、车间产线、设备台账、
/// 产品与物料、客户供应商。
///
/// 约束：①只创建结构性主数据，绝不创建结果事实；②重复执行幂等（存在即跳过）；③与租户已有
/// 同号段数据冲突时直接失败而不覆盖；④永不触碰 MAN-519 固定演示事实（<c>*-DEMO-*</c>）与
/// 千单规模块（<c>*-SCALE-*</c>）号段；⑤分批 <c>SaveChanges</c>，控制启动期变更跟踪器规模。
/// </summary>
public sealed class WorldBibleSeedService(ApplicationDbContext dbContext)
{
    private const string TeamDepartmentCode = "DEPT-PROD";

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        await SeedSiteAndShiftsAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedFactoryStructureAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedDevicesAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedSkusAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedPartnersAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedOrganizationAndPeopleAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    #region §1 厂区 / 班次 / 部门

    private async Task SeedSiteAndShiftsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var site = await dbContext.Sites.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == WorldBibleSpec.SiteCode,
            cancellationToken);
        if (site is null)
        {
            dbContext.Sites.Add(Site.Create(
                organizationId, environmentId, WorldBibleSpec.SiteCode, WorldBibleSpec.SiteName, WorldBibleSpec.SiteTimezone));
        }
        else if (site.Name != WorldBibleSpec.SiteName || site.Timezone != WorldBibleSpec.SiteTimezone || site.Disabled)
        {
            throw Collision(WorldBibleSpec.SiteCode);
        }

        foreach (var shift in WorldBibleSpec.Shifts)
        {
            var existing = await dbContext.Shifts.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == shift.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.Shifts.Add(Shift.Create(
                    organizationId, environmentId, shift.Code, shift.Name, shift.StartsAt, shift.EndsAt, shift.PaidMinutes));
            }
            else if (existing.Name != shift.Name || existing.StartsAt != shift.StartsAt || existing.EndsAt != shift.EndsAt)
            {
                throw Collision(shift.Code);
            }
        }

        foreach (var department in WorldBibleSpec.Departments)
        {
            if (!await dbContext.Departments.AnyAsync(x =>
                    x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == department.Code,
                    cancellationToken))
            {
                dbContext.Departments.Add(Department.Create(
                    organizationId, environmentId, department.Code, department.Name, null));
            }
        }
    }

    #endregion

    #region §2 车间 / 产线 / 工作中心

    private async Task SeedFactoryStructureAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var workshop in WorldBibleSpec.Workshops)
        {
            var existing = await dbContext.Workshops.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == workshop.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.Workshops.Add(Workshop.Create(
                    organizationId, environmentId, workshop.Code, workshop.Name, WorldBibleSpec.SiteCode, null, workshop.Description));
            }
            else if (existing.Name != workshop.Name || existing.SiteCode != WorldBibleSpec.SiteCode || existing.Disabled)
            {
                throw Collision(workshop.Code);
            }
        }

        foreach (var line in WorldBibleSpec.ProductionLines)
        {
            var existing = await dbContext.ProductionLines.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == line.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.ProductionLines.Add(ProductionLine.Create(
                    organizationId, environmentId, line.Code, line.Name, WorldBibleSpec.SiteCode, line.WorkshopCode));
            }
            else if (existing.Name != line.Name || existing.SiteCode != WorldBibleSpec.SiteCode ||
                     existing.WorkshopCode != line.WorkshopCode || existing.Disabled)
            {
                throw Collision(line.Code);
            }
        }

        foreach (var workCenter in WorldBibleSpec.WorkCenters)
        {
            var existing = await dbContext.WorkCenters.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == workCenter.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                    organizationId,
                    environmentId,
                    workCenter.Code,
                    workCenter.Name,
                    workCenter.CapacityMinutesPerDay,
                    "work-center",
                    WorldBibleSpec.SiteCode,
                    workCenter.LineCode,
                    workCenter.WorkshopCode,
                    WorldBibleSpec.CalendarCode,
                    "minute",
                    finiteCapacity: true,
                    numberOfCapacities: workCenter.NumberOfCapacities));
            }
            else if (existing.Name != workCenter.Name || existing.PlantCode != WorldBibleSpec.SiteCode ||
                     existing.LineCode != workCenter.LineCode || existing.DefaultCalendarCode != WorldBibleSpec.CalendarCode ||
                     existing.Disabled)
            {
                throw Collision(workCenter.Code);
            }
        }
    }

    #endregion

    #region §3 设备台账

    private async Task SeedDevicesAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var workCentersByCode = WorldBibleSpec.WorkCenters.ToDictionary(x => x.Code, StringComparer.Ordinal);
        foreach (var device in WorldBibleSpec.Devices)
        {
            var workCenter = workCentersByCode[device.WorkCenterCode];
            var existing = await dbContext.DeviceAssets.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == device.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.DeviceAssets.Add(DeviceAsset.RegisterCapability(
                        organizationId,
                        environmentId,
                        device.Code,
                        device.Model,
                        workCenter.LineCode,
                        device.WorkCenterCode,
                        device.AssetClassCode,
                        device.Manufacturer,
                        serialNo: string.Empty,
                        minimumCapacity: null,
                        maximumCapacity: null,
                        capacityUomCode: string.Empty,
                        device.Criticality,
                        maintainable: true,
                        telemetryEnabled: true,
                        externalReferences: new Dictionary<string, string>())
                    .WithLedger(
                        purchaseDate: null,
                        purchaseCost: null,
                        purchaseCurrencyCode: string.Empty,
                        warrantyExpiresOn: null,
                        supplierPartnerCode: string.Empty,
                        siteCode: WorldBibleSpec.SiteCode,
                        workshopCode: workCenter.WorkshopCode,
                        lineCode: workCenter.LineCode,
                        stationCode: string.Empty,
                        parentDeviceId: null,
                        retiredOn: null));
                continue;
            }

            if (existing.Model != device.Model || existing.WorkCenterCode != device.WorkCenterCode ||
                existing.LineCode != workCenter.LineCode || existing.Disabled)
            {
                throw Collision(device.Code);
            }
        }
    }

    #endregion

    #region §4 产品与物料

    private async Task SeedSkusAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var sku in WorldBibleSpec.AllSkus)
        {
            var existing = await dbContext.Skus.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == sku.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.Skus.Add(Sku.Create(organizationId, environmentId, sku.Code, sku.Name, sku.Unit, sku.Category));
                continue;
            }

            if (existing.Name != sku.Name || existing.Unit != sku.Unit || existing.Category != sku.Category || existing.Disabled)
            {
                throw Collision(sku.Code);
            }
        }
    }

    #endregion

    #region §6 客户与供应商

    private async Task SeedPartnersAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var partner in WorldBibleSpec.Customers.Concat(WorldBibleSpec.Suppliers))
        {
            var existing = await dbContext.BusinessPartners.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == partner.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.BusinessPartners.Add(BusinessPartner.Create(
                    organizationId, environmentId, partner.Code, partner.PartnerType, partner.Name));
                continue;
            }

            if (existing.Name != partner.Name || existing.PartnerType != partner.PartnerType || existing.Disabled)
            {
                throw Collision(partner.Code);
            }
        }
    }

    #endregion

    #region §5 技能 / 班组 / 员工

    private async Task SeedOrganizationAndPeopleAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var skill in WorldBibleSpec.Skills)
        {
            if (!await dbContext.Skills.AnyAsync(x =>
                    x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.SkillCode == skill.Code,
                    cancellationToken))
            {
                dbContext.Skills.Add(Skill.Create(
                    organizationId,
                    environmentId,
                    skill.Code,
                    skill.Name,
                    skill.GroupName,
                    skill.RequiresCertification,
                    skill.ValidityMonths,
                    skill.Description));
            }
        }

        foreach (var team in WorldBibleSpec.Teams)
        {
            var existing = await dbContext.Teams.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == team.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.Teams.Add(Team.Create(
                    organizationId, environmentId, team.Code, team.Name, TeamDepartmentCode, team.ShiftCode, team.WorkshopCode));
            }
            else if (existing.Name != team.Name || existing.ShiftCode != team.ShiftCode || existing.Disabled)
            {
                throw Collision(team.Code);
            }
            else if (existing.WorkshopCode is null)
            {
                // 车间归属是本次新增的事实，旧环境补挂即可，不算冲突。
                existing.Update(existing.Name, existing.DepartmentCode, existing.ShiftCode, team.WorkshopCode);
            }
        }

        // 技能目录与班组先落库，再写 58 人的成员/技能绑定：两段分批，避免单次 SaveChanges 过大。
        await dbContext.SaveChangesAsync(cancellationToken);

        // 员工档案（工号/姓名/部门/岗位/在岗）落 MasterData —— 「人」的业务权威在这里；
        // IAM 侧同名字段只是账号展示冗余，见 Iam WorldBibleWorkerSpec 的说明。
        foreach (var employee in WorldBibleSpec.Employees)
        {
            if (await dbContext.Workers.AnyAsync(x =>
                    x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.UserId == employee.UserId,
                    cancellationToken))
            {
                continue;
            }

            dbContext.Workers.Add(Worker.Create(
                organizationId,
                environmentId,
                employee.EmployeeNo,
                employee.Name,
                employee.UserId,
                employee.DepartmentCode,
                employee.RoleName,
                Worker.StatusActive,
                null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        for (var ordinal = 0; ordinal < WorldBibleSpec.Employees.Count; ordinal++)
        {
            var employee = WorldBibleSpec.Employees[ordinal];
            if (employee.TeamCode is not null &&
                !await dbContext.TeamMembers.AnyAsync(x =>
                    x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.TeamCode == employee.TeamCode && x.UserId == employee.UserId,
                    cancellationToken))
            {
                dbContext.TeamMembers.Add(TeamMember.Assign(
                    organizationId,
                    environmentId,
                    employee.TeamCode,
                    employee.UserId,
                    employee.IsTeamLeader,
                    WorldBibleSpec.GoLiveDate,
                    WorldBibleSpec.AssignmentValidTo));
            }

            foreach (var skillCode in employee.SkillCodes)
            {
                if (await dbContext.PersonnelSkills.AnyAsync(x =>
                        x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        x.UserId == employee.UserId && x.SkillCode == skillCode,
                        cancellationToken))
                {
                    continue;
                }

                dbContext.PersonnelSkills.Add(PersonnelSkill.Assign(
                    organizationId,
                    environmentId,
                    employee.UserId,
                    skillCode,
                    WorldBibleSpec.SkillLevel(ordinal),
                    WorldBibleSpec.GoLiveDate,
                    WorldBibleSpec.AssignmentValidTo));
            }
        }
    }

    #endregion

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved world-bible master-data fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
