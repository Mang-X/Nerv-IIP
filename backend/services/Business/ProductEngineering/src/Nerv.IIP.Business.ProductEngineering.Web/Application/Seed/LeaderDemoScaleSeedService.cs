using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// MAN-519 白名单内的领导演示「规模块」工程事实：为每个规模 SKU 发布一条 MBOM、一条 4 道工序的
/// 工艺路线和一个 active 生产版本，使千单级工单具备真实可排的前后置工序形状。
/// 使用独立 <c>*-SCALE-*</c> 号段，绝不触碰 <c>MBOM-DEMO-001</c>/<c>ROUTING-DEMO-001</c> 等固定演示事实。
/// </summary>
public sealed class LeaderDemoScaleSeedService(ApplicationDbContext dbContext)
{
    public const string Revision = "1";
    private static readonly DateOnly EffectiveDate = new(2026, 7, 1);

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

        foreach (var sku in LeaderDemoScaleSpec.FinishedSkus)
        {
            await SeedMbomAsync(organizationId, environmentId, sku, cancellationToken);
            await SeedRoutingAsync(organizationId, environmentId, sku, cancellationToken);
            await SeedProductionVersionAsync(organizationId, environmentId, sku, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedMbomAsync(
        string organizationId,
        string environmentId,
        LeaderDemoScaleSku sku,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ManufacturingBoms
            .Include(x => x.MaterialLines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.BomCode == sku.MbomCode && x.Revision == Revision,
                cancellationToken);
        if (existing is null)
        {
            var mbom = ManufacturingBom.CreateDraft(organizationId, environmentId, sku.MbomCode, Revision, sku.SkuCode)
                .AddMaterialLine(LeaderDemoScaleSpec.RawMaterialSkuCode, 1m, "pcs", 0m);
            mbom.ReleaseFromEngineeringBom($"EBOM-SCALE-{sku.SkuCode}:1", EngineeringVersionStatus.Published, EffectiveDate);
            dbContext.ManufacturingBoms.Add(mbom);
            return;
        }

        if (existing.SkuCode != sku.SkuCode || existing.Status != EngineeringVersionStatus.Published ||
            existing.EffectiveDate != EffectiveDate || existing.MaterialLines.Count != 1 ||
            existing.MaterialLines.Single().SkuCode != LeaderDemoScaleSpec.RawMaterialSkuCode)
        {
            throw Collision($"{sku.MbomCode}:{Revision}");
        }
    }

    private async Task SeedRoutingAsync(
        string organizationId,
        string environmentId,
        LeaderDemoScaleSku sku,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Routings
            .Include(x => x.Operations)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.RoutingCode == sku.RoutingCode && x.Revision == Revision,
                cancellationToken);
        if (existing is null)
        {
            var routing = Routing.CreateDraft(organizationId, environmentId, sku.RoutingCode, Revision, sku.SkuCode);
            foreach (var stage in LeaderDemoScaleSpec.Stages)
            {
                routing = routing.AddOperation(
                    stage.Sequence,
                    stage.WorkCenterCode,
                    stage.OperationCode,
                    stage.OperationName,
                    stage.SetupMinutes,
                    stage.RunMinutes,
                    stage.TeardownMinutes,
                    controlKey: "standard",
                    requiresReporting: true,
                    requiresQualityInspection: false,
                    isOutsourced: false);
            }

            routing.Release(EffectiveDate);
            dbContext.Routings.Add(routing);
            return;
        }

        var operations = existing.Operations.OrderBy(x => x.Sequence).ToArray();
        if (existing.SkuCode != sku.SkuCode || existing.Status != EngineeringVersionStatus.Published ||
            existing.EffectiveDate != EffectiveDate || operations.Length != LeaderDemoScaleSpec.Stages.Length ||
            operations.Where((operation, index) =>
                operation.Sequence != LeaderDemoScaleSpec.Stages[index].Sequence ||
                operation.WorkCenterCode != LeaderDemoScaleSpec.Stages[index].WorkCenterCode ||
                operation.OperationCode != LeaderDemoScaleSpec.Stages[index].OperationCode).Any())
        {
            throw Collision($"{sku.RoutingCode}:{Revision}");
        }
    }

    private async Task SeedProductionVersionAsync(
        string organizationId,
        string environmentId,
        LeaderDemoScaleSku sku,
        CancellationToken cancellationToken)
    {
        var mbomVersionId = $"{sku.MbomCode}:{Revision}";
        var routingVersionId = $"{sku.RoutingCode}:{Revision}";
        var versions = await dbContext.ProductionVersions
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.SkuCode == sku.SkuCode && x.Status == ProductionVersionStatus.Active)
            .ToArrayAsync(cancellationToken);
        if (versions.Length == 0)
        {
            dbContext.ProductionVersions.Add(ProductionVersion.Create(
                organizationId, environmentId, sku.SkuCode, mbomVersionId, routingVersionId, EffectiveDate,
                null, null, null, 0, true, EngineeringVersionStatus.Published, EngineeringVersionStatus.Published));
            return;
        }

        if (versions.Length != 1 || versions[0].MbomVersionId != mbomVersionId ||
            versions[0].RoutingVersionId != routingVersionId || versions[0].ValidFrom != EffectiveDate ||
            versions[0].ValidTo is not null || !versions[0].IsDefault)
        {
            throw Collision($"active production version for {sku.SkuCode}");
        }
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo scale engineering fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
