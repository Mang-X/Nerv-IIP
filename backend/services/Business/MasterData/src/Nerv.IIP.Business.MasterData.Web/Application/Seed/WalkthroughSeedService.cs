using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public sealed class WalkthroughSeedService(ApplicationDbContext dbContext)
{
    /// <summary>
    /// 只写入人工走查需要的结构与小目录；不写员工或任何交易结果事实。
    /// </summary>
    public async Task SeedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        await SeedSiteAndShiftsAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedFactoryStructureAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedSkusAsync(organizationId, environmentId, WalkthroughSeedSpec.Skus, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedPartnersAsync(
            organizationId,
            environmentId,
            WalkthroughSeedSpec.Customers.Concat(WalkthroughSeedSpec.Suppliers),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    #region §1 厂区 / 班次 / 部门

    private async Task SeedSiteAndShiftsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var site = await dbContext.Sites.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == WalkthroughSeedSpec.SiteCode,
            cancellationToken);
        if (site is null)
        {
            dbContext.Sites.Add(Site.Create(
                organizationId, environmentId, WalkthroughSeedSpec.SiteCode, WalkthroughSeedSpec.SiteName, WalkthroughSeedSpec.SiteTimezone));
        }
        else if (site.Name != WalkthroughSeedSpec.SiteName || site.Timezone != WalkthroughSeedSpec.SiteTimezone || site.Disabled)
        {
            throw Collision(WalkthroughSeedSpec.SiteCode);
        }

        foreach (var shift in WalkthroughSeedSpec.Shifts)
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

        foreach (var department in WalkthroughSeedSpec.Departments)
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
        foreach (var workshop in WalkthroughSeedSpec.Workshops)
        {
            var existing = await dbContext.Workshops.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == workshop.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.Workshops.Add(Workshop.Create(
                    organizationId, environmentId, workshop.Code, workshop.Name, WalkthroughSeedSpec.SiteCode, null, workshop.Description));
            }
            else if (existing.Name != workshop.Name || existing.SiteCode != WalkthroughSeedSpec.SiteCode || existing.Disabled)
            {
                throw Collision(workshop.Code);
            }
        }

        foreach (var line in WalkthroughSeedSpec.ProductionLines)
        {
            var existing = await dbContext.ProductionLines.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == line.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.ProductionLines.Add(ProductionLine.Create(
                    organizationId, environmentId, line.Code, line.Name, WalkthroughSeedSpec.SiteCode, line.WorkshopCode));
            }
            else if (existing.Name != line.Name || existing.SiteCode != WalkthroughSeedSpec.SiteCode ||
                     existing.WorkshopCode != line.WorkshopCode || existing.Disabled)
            {
                throw Collision(line.Code);
            }
        }

        foreach (var workCenter in WalkthroughSeedSpec.WorkCenters)
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
                    WalkthroughSeedSpec.SiteCode,
                    workCenter.LineCode,
                    workCenter.WorkshopCode,
                    WalkthroughSeedSpec.CalendarCode,
                    "minute",
                    finiteCapacity: true,
                    numberOfCapacities: workCenter.NumberOfCapacities));
            }
            else if (existing.Name != workCenter.Name || existing.PlantCode != WalkthroughSeedSpec.SiteCode ||
                     existing.LineCode != workCenter.LineCode || existing.DefaultCalendarCode != WalkthroughSeedSpec.CalendarCode ||
                     existing.Disabled)
            {
                throw Collision(workCenter.Code);
            }
        }

        foreach (var device in WalkthroughSeedSpec.Devices)
        {
            if (await dbContext.DeviceAssets.AnyAsync(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId && x.Code == device.Code, cancellationToken))
            {
                continue;
            }

            var center = WalkthroughSeedSpec.WorkCenters.Single(x => x.Code == device.WorkCenterCode);
            var asset = DeviceAsset.RegisterCapability(organizationId, environmentId,
                device.Code, device.Name, center.LineCode, center.Code, "equipment", "", "", null, null, "",
                "normal", maintainable: true, telemetryEnabled: true, new Dictionary<string, string>())
                .WithLedger(null, null, "", null, "", WalkthroughSeedSpec.SiteCode, center.WorkshopCode, center.LineCode, "", null, null);
            dbContext.DeviceAssets.Add(asset);
        }
    }

    #endregion

    #region §4 产品与物料

    private async Task SeedSkusAsync(
        string organizationId,
        string environmentId,
        IEnumerable<WalkthroughSku> skus,
        CancellationToken cancellationToken)
    {
        foreach (var sku in skus)
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

    private async Task SeedPartnersAsync(
        string organizationId,
        string environmentId,
        IEnumerable<WalkthroughPartner> partners,
        CancellationToken cancellationToken)
    {
        foreach (var partner in partners)
        {
            var existing = await dbContext.BusinessPartners.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == partner.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.BusinessPartners.Add(BusinessPartner.Create(
                    organizationId,
                    environmentId,
                    partner.Code,
                    partner.PartnerType,
                    partner.Name,
                    [partner.PartnerType],
                    taxId: null,
                    creditLimit: partner.CreditLimit,
                    creditCurrencyCode: partner.CreditCurrencyCode));
                continue;
            }

            if (existing.Name != partner.Name || existing.PartnerType != partner.PartnerType || existing.Disabled)
            {
                throw Collision(partner.Code);
            }

            // #1290 信用额度只补缺失：租户已维护的额度（含手工清空后重设）一律不动。
            if (existing.CreditLimit is null && partner.CreditLimit is not null)
            {
                existing.UpdateCreditLimit(partner.CreditLimit, partner.CreditCurrencyCode);
            }
        }
    }

    #endregion

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved walkthrough master-data fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
