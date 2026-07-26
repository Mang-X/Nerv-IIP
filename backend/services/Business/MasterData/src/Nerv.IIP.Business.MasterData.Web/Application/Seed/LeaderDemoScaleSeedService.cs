using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

/// <summary>
/// MAN-519 白名单内的领导演示「规模块」主数据前置事实：多工序工作中心、24 台可排设备资源和多 SKU。
/// 该块使用独立 <c>*-SCALE-*</c> 号段，永不触碰 <c>WC-CNC-DEMO</c>、<c>DEV-CNC-DEMO</c> 等固定演示事实；
/// 重复执行幂等（存在即跳过），与租户维护的数据冲突时直接失败而不覆盖。
/// </summary>
public sealed class LeaderDemoScaleSeedService(ApplicationDbContext dbContext)
{
    public async Task SeedAsync(
        string organizationId,
        string environmentId,
        int orderCount,
        CancellationToken cancellationToken = default)
    {
        if (orderCount <= 0)
        {
            return;
        }

        var line = await dbContext.ProductionLines.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == LeaderDemoScaleSpec.LineCode,
            cancellationToken);
        if (line is null)
        {
            dbContext.ProductionLines.Add(ProductionLine.Create(
                organizationId, environmentId, LeaderDemoScaleSpec.LineCode, LeaderDemoScaleSpec.LineName, LeaderDemoScaleSpec.SiteCode));
        }
        else if (line.Name != LeaderDemoScaleSpec.LineName || line.SiteCode != LeaderDemoScaleSpec.SiteCode || line.Disabled)
        {
            throw Collision(LeaderDemoScaleSpec.LineCode);
        }

        foreach (var workCenter in LeaderDemoScaleSpec.WorkCenters)
        {
            await SeedWorkCenterAsync(organizationId, environmentId, workCenter, cancellationToken);
            for (var index = 1; index <= LeaderDemoScaleSpec.DevicesPerWorkCenter; index++)
            {
                await SeedDeviceAsync(organizationId, environmentId, workCenter, index, cancellationToken);
            }
        }

        foreach (var sku in LeaderDemoScaleSpec.FinishedSkus)
        {
            await SeedSkuAsync(organizationId, environmentId, sku.Code, sku.Name, "finished-goods", cancellationToken);
        }

        await SeedSkuAsync(
            organizationId,
            environmentId,
            LeaderDemoScaleSpec.RawMaterialSkuCode,
            LeaderDemoScaleSpec.RawMaterialSkuName,
            "raw-material",
            cancellationToken);

        foreach (var customer in LeaderDemoScaleSpec.Customers)
        {
            await SeedCustomerAsync(organizationId, environmentId, customer, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedWorkCenterAsync(
        string organizationId,
        string environmentId,
        LeaderDemoScaleWorkCenter spec,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.WorkCenters.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == spec.Code,
            cancellationToken);
        if (existing is null)
        {
            dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                organizationId,
                environmentId,
                spec.Code,
                spec.Name,
                LeaderDemoScaleSpec.CapacityMinutesPerDay,
                "work-center",
                LeaderDemoScaleSpec.SiteCode,
                LeaderDemoScaleSpec.LineCode,
                workshopCode: null,
                LeaderDemoScaleSpec.CalendarCode,
                "minute",
                finiteCapacity: true,
                numberOfCapacities: LeaderDemoScaleSpec.DevicesPerWorkCenter));
            return;
        }

        if (existing.Name != spec.Name || existing.PlantCode != LeaderDemoScaleSpec.SiteCode ||
            existing.LineCode != LeaderDemoScaleSpec.LineCode || existing.DefaultCalendarCode != LeaderDemoScaleSpec.CalendarCode ||
            existing.Disabled)
        {
            throw Collision(spec.Code);
        }
    }

    private async Task SeedDeviceAsync(
        string organizationId,
        string environmentId,
        LeaderDemoScaleWorkCenter spec,
        int index,
        CancellationToken cancellationToken)
    {
        var code = LeaderDemoScaleSpec.DeviceCode(spec, index);
        var model = $"{spec.DeviceModel}-{index:D2}";
        var existing = await dbContext.DeviceAssets.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code,
            cancellationToken);
        if (existing is null)
        {
            dbContext.DeviceAssets.Add(DeviceAsset.RegisterCapability(
                organizationId,
                environmentId,
                code,
                model,
                LeaderDemoScaleSpec.LineCode,
                spec.Code,
                spec.AssetClassCode,
                manufacturer: string.Empty,
                serialNo: string.Empty,
                minimumCapacity: null,
                maximumCapacity: null,
                capacityUomCode: string.Empty,
                criticality: "medium",
                maintainable: true,
                telemetryEnabled: false,
                externalReferences: new Dictionary<string, string>()));
            return;
        }

        if (existing.Model != model || existing.LineCode != LeaderDemoScaleSpec.LineCode ||
            existing.WorkCenterCode != spec.Code || existing.Disabled)
        {
            throw Collision(code);
        }
    }

    private async Task SeedSkuAsync(
        string organizationId,
        string environmentId,
        string code,
        string name,
        string category,
        CancellationToken cancellationToken)
    {
        var sku = await dbContext.Skus.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code,
            cancellationToken);
        if (sku is null)
        {
            dbContext.Skus.Add(Sku.Create(organizationId, environmentId, code, name, "pcs", category));
            return;
        }

        if (sku.Name != name || sku.Unit != "pcs" || sku.Category != category || sku.Disabled)
        {
            throw Collision(code);
        }
    }

    private async Task SeedCustomerAsync(
        string organizationId,
        string environmentId,
        LeaderDemoScaleSku customer,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.BusinessPartners.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == customer.Code,
            cancellationToken);
        if (existing is null)
        {
            dbContext.BusinessPartners.Add(BusinessPartner.Create(
                organizationId, environmentId, customer.Code, "customer", customer.Name));
            return;
        }

        if (existing.Name != customer.Name || existing.PartnerType != "customer" || existing.Disabled)
        {
            throw Collision(customer.Code);
        }
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo scale master-data fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
