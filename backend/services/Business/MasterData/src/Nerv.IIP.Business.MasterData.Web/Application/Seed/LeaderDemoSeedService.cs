using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
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

        var workCenter = await dbContext.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "WC-CNC-DEMO", cancellationToken);
        if (workCenter is null)
        {
            dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                organizationId, environmentId, "WC-CNC-DEMO", "CNC 精加工中心", 480, "work-center",
                "SITE-001", "LINE-DEMO-01", "STANDARD", "minute", true));
        }
        else if (workCenter.Name != "CNC 精加工中心" || workCenter.CapacityMinutesPerDay != 480 ||
                 workCenter.PlantCode != "SITE-001" || workCenter.LineCode != "LINE-DEMO-01" || workCenter.Disabled)
        {
            throw Collision("WC-CNC-DEMO");
        }

        // The CNC work center needs a staffing team so MES dispatch can offer work-center scoped
        // candidates; without the team binding the dispatch picker has nothing to narrow down to.
        var cncTeam = await dbContext.Teams.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "TEAM-CNC-DEMO", cancellationToken);
        if (cncTeam is null)
        {
            dbContext.Teams.Add(Team.Create(organizationId, environmentId, "TEAM-CNC-DEMO", "CNC 精加工班组", "DEPT-PROD", "DAY", "WC-CNC-DEMO"));
        }
        else if (cncTeam.WorkCenterCode is null)
        {
            cncTeam.Update(cncTeam.Name, cncTeam.DepartmentCode, cncTeam.ShiftCode, "WC-CNC-DEMO");
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

        var customer = await dbContext.BusinessPartners.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "CUST-DEMO-001", cancellationToken);
        if (customer is null)
        {
            dbContext.BusinessPartners.Add(BusinessPartner.Create(organizationId, environmentId, "CUST-DEMO-001", "customer", "华东汽车零部件采购中心"));
        }
        else if (customer.Name != "华东汽车零部件采购中心" || customer.PartnerType != "customer" || customer.Disabled)
        {
            throw Collision("CUST-DEMO-001");
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

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo master-data fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
