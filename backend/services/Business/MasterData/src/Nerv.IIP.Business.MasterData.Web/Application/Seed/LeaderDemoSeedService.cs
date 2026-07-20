using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
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
            dbContext.Sites.Add(Site.Create(organizationId, environmentId, "SITE-001", "Leader Demo Site", "Asia/Shanghai"));
        }
        else if (site.Name != "Leader Demo Site" || site.Timezone != "Asia/Shanghai" || site.Disabled)
        {
            throw Collision("SITE-001");
        }

        var line = await dbContext.ProductionLines.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "LINE-DEMO-01", cancellationToken);
        if (line is null)
        {
            dbContext.ProductionLines.Add(ProductionLine.Create(organizationId, environmentId, "LINE-DEMO-01", "Leader Demo Line", "SITE-001"));
        }
        else if (line.Name != "Leader Demo Line" || line.SiteCode != "SITE-001" || line.WorkshopCode is not null || line.Disabled)
        {
            throw Collision("LINE-DEMO-01");
        }

        var workCenter = await dbContext.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "WC-CNC-DEMO", cancellationToken);
        if (workCenter is null)
        {
            dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                organizationId, environmentId, "WC-CNC-DEMO", "Leader Demo CNC Work Center", 480, "work-center",
                "SITE-001", "LINE-DEMO-01", "STANDARD", "minute", true));
        }
        else if (workCenter.Name != "Leader Demo CNC Work Center" || workCenter.CapacityMinutesPerDay != 480 ||
                 workCenter.PlantCode != "SITE-001" || workCenter.LineCode != "LINE-DEMO-01" || workCenter.Disabled)
        {
            throw Collision("WC-CNC-DEMO");
        }

        await SeedSkuAsync(organizationId, environmentId, "SKU-DEMO-001", "Leader Demo Finished Product", "finished-goods", cancellationToken);
        await SeedSkuAsync(organizationId, environmentId, "SKU-DEMO-RM-001", "Leader Demo Raw Material", "raw-material", cancellationToken);

        var customer = await dbContext.BusinessPartners.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "CUST-DEMO-001", cancellationToken);
        if (customer is null)
        {
            dbContext.BusinessPartners.Add(BusinessPartner.Create(organizationId, environmentId, "CUST-DEMO-001", "customer", "Leader Demo Customer"));
        }
        else if (customer.Name != "Leader Demo Customer" || customer.PartnerType != "customer" || customer.Disabled)
        {
            throw Collision("CUST-DEMO-001");
        }

        var device = await dbContext.DeviceAssets.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "DEV-CNC-DEMO", cancellationToken);
        if (device is null)
        {
            dbContext.DeviceAssets.Add(DeviceAsset.RegisterCapability(
                organizationId, environmentId, "DEV-CNC-DEMO", "Leader Demo CNC", "LINE-DEMO-01", "WC-CNC-DEMO",
                "cnc", "", "", null, null, "", "high", true, true, new Dictionary<string, string>()));
        }
        else if (device.Model != "Leader Demo CNC" || device.LineCode != "LINE-DEMO-01" || device.WorkCenterCode != "WC-CNC-DEMO" ||
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
