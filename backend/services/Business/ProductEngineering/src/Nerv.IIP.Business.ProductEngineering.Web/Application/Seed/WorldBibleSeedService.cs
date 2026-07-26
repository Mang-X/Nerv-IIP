using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§4 的 ProductEngineering 侧种子：8 道标准工序 + 24 个成品的
/// EBOM / MBOM / 工艺路线 / 生产版本（V1 全量，热销 8 款附 V2 换弹簧供应商的版本演进）。
///
/// 约束与 MasterData 侧一致：只创建结构性工程主数据、幂等、冲突不覆盖、绝不触碰
/// <c>*-DEMO-*</c> 与 <c>*-SCALE-*</c> 号段；按成品分批 <c>SaveChanges</c>。
/// </summary>
public sealed class WorldBibleSeedService(ApplicationDbContext dbContext)
{
    /// <summary>每写入多少个成品落一次盘，控制启动期变更跟踪器规模。</summary>
    private const int SaveBatchSize = 6;

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        await SeedStandardOperationsAsync(organizationId, environmentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var pending = 0;
        foreach (var product in WorldBibleSpec.Products)
        {
            await SeedProductAsync(organizationId, environmentId, product, WorldBibleSpec.V1Revision, cancellationToken);
            if (product.IsHotSelling)
            {
                await SeedProductAsync(organizationId, environmentId, product, WorldBibleSpec.V2Revision, cancellationToken);
            }

            await SeedProductionVersionsAsync(organizationId, environmentId, product, cancellationToken);
            if (++pending >= SaveBatchSize)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                pending = 0;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedStandardOperationsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        foreach (var operation in WorldBibleSpec.StandardOperations)
        {
            if (await dbContext.StandardOperations.AnyAsync(x =>
                    x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    x.OperationCode == operation.OperationCode,
                    cancellationToken))
            {
                continue;
            }

            dbContext.StandardOperations.Add(StandardOperation.Create(
                organizationId,
                environmentId,
                operation.OperationCode,
                operation.OperationName,
                operation.DefaultWorkCenterCode,
                operation.SetupMinutes,
                operation.RunMinutes,
                controlKey: "standard",
                requiresReporting: true,
                operation.RequiresQualityInspection,
                isOutsourced: false,
                description: null));
        }
    }

    private async Task SeedProductAsync(
        string organizationId,
        string environmentId,
        WorldBibleProduct product,
        string revision,
        CancellationToken cancellationToken)
    {
        var effectiveDate = revision == WorldBibleSpec.V2Revision
            ? WorldBibleSpec.V2EffectiveDate
            : WorldBibleSpec.V1EffectiveDate;

        await SeedEngineeringItemAsync(organizationId, environmentId, product, revision, cancellationToken);
        await SeedEngineeringBomAsync(organizationId, environmentId, product, revision, effectiveDate, cancellationToken);
        await SeedManufacturingBomAsync(organizationId, environmentId, product, revision, effectiveDate, cancellationToken);

        // 工艺路线只有 V1：设定集 §4 的 V2 只换弹簧供应商，工序不变。
        if (revision == WorldBibleSpec.V1Revision)
        {
            await SeedRoutingAsync(organizationId, environmentId, product, cancellationToken);
        }
    }

    private async Task SeedEngineeringItemAsync(
        string organizationId,
        string environmentId,
        WorldBibleProduct product,
        string revision,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.EngineeringItems.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
            x.ItemCode == product.SkuCode && x.Revision == revision,
            cancellationToken);
        if (existing is null)
        {
            dbContext.EngineeringItems.Add(EngineeringItem.CreateRevision(
                organizationId, environmentId, product.SkuCode, revision, product.SkuName, release: true));
            return;
        }

        if (existing.Name != product.SkuName)
        {
            throw Collision($"{product.SkuCode}:{revision}");
        }
    }

    private async Task SeedEngineeringBomAsync(
        string organizationId,
        string environmentId,
        WorldBibleProduct product,
        string revision,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var bomCode = WorldBibleSpec.EngineeringBomCode(product.SkuCode);
        var existing = await dbContext.EngineeringBoms
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.BomCode == bomCode && x.Revision == revision,
                cancellationToken);
        var lines = product.EngineeringLines(revision);
        if (existing is null)
        {
            var bom = EngineeringBom.CreateDraft(organizationId, environmentId, bomCode, revision, product.SkuCode);
            foreach (var line in lines)
            {
                bom = bom.AddLine(line.ComponentSkuCode, line.Quantity, line.UnitOfMeasureCode, scrapRate: line.ScrapRate);
            }

            bom.Release(effectiveDate);
            dbContext.EngineeringBoms.Add(bom);
            return;
        }

        if (existing.ParentItemCode != product.SkuCode || existing.Status != EngineeringVersionStatus.Published ||
            existing.EffectiveDate != effectiveDate || existing.Lines.Count != lines.Count)
        {
            throw Collision(WorldBibleSpec.VersionId(bomCode, revision));
        }
    }

    private async Task SeedManufacturingBomAsync(
        string organizationId,
        string environmentId,
        WorldBibleProduct product,
        string revision,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var bomCode = WorldBibleSpec.ManufacturingBomCode(product.SkuCode);
        var existing = await dbContext.ManufacturingBoms
            .Include(x => x.MaterialLines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.BomCode == bomCode && x.Revision == revision,
                cancellationToken);
        var lines = product.ManufacturingLines(revision);
        if (existing is null)
        {
            var mbom = ManufacturingBom.CreateDraft(organizationId, environmentId, bomCode, revision, product.SkuCode);
            foreach (var line in lines)
            {
                mbom = mbom.AddMaterialLine(line.ComponentSkuCode, line.Quantity, line.UnitOfMeasureCode, line.ScrapRate);
            }

            mbom.ReleaseFromEngineeringBom(
                WorldBibleSpec.VersionId(WorldBibleSpec.EngineeringBomCode(product.SkuCode), revision),
                EngineeringVersionStatus.Published,
                effectiveDate);
            dbContext.ManufacturingBoms.Add(mbom);
            return;
        }

        if (existing.SkuCode != product.SkuCode || existing.Status != EngineeringVersionStatus.Published ||
            existing.EffectiveDate != effectiveDate || existing.MaterialLines.Count != lines.Count)
        {
            throw Collision(WorldBibleSpec.VersionId(bomCode, revision));
        }
    }

    private async Task SeedRoutingAsync(
        string organizationId,
        string environmentId,
        WorldBibleProduct product,
        CancellationToken cancellationToken)
    {
        var routingCode = WorldBibleSpec.RoutingCode(product.SkuCode);
        var stages = product.RoutingStages();
        var existing = await dbContext.Routings
            .Include(x => x.Operations)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.RoutingCode == routingCode && x.Revision == WorldBibleSpec.V1Revision,
                cancellationToken);
        if (existing is null)
        {
            var routing = Routing.CreateDraft(
                organizationId, environmentId, routingCode, WorldBibleSpec.V1Revision, product.SkuCode);
            foreach (var stage in stages)
            {
                routing = routing.AddOperation(
                    stage.Operation.Sequence,
                    stage.WorkCenterCode,
                    stage.Operation.OperationCode,
                    stage.Operation.OperationName,
                    stage.Operation.SetupMinutes,
                    stage.Operation.RunMinutes,
                    stage.Operation.TeardownMinutes,
                    controlKey: "standard",
                    requiresReporting: true,
                    stage.Operation.RequiresQualityInspection,
                    isOutsourced: false);
            }

            routing.Release(WorldBibleSpec.V1EffectiveDate);
            dbContext.Routings.Add(routing);
            return;
        }

        var operations = existing.Operations.OrderBy(x => x.Sequence).ToArray();
        if (existing.SkuCode != product.SkuCode || existing.Status != EngineeringVersionStatus.Published ||
            operations.Length != stages.Count ||
            operations.Where((operation, index) =>
                operation.Sequence != stages[index].Operation.Sequence ||
                operation.WorkCenterCode != stages[index].WorkCenterCode).Any())
        {
            throw Collision(WorldBibleSpec.VersionId(routingCode, WorldBibleSpec.V1Revision));
        }
    }

    private async Task SeedProductionVersionsAsync(
        string organizationId,
        string environmentId,
        WorldBibleProduct product,
        CancellationToken cancellationToken)
    {
        var routingVersionId = WorldBibleSpec.VersionId(
            WorldBibleSpec.RoutingCode(product.SkuCode), WorldBibleSpec.V1Revision);
        var mbomCode = WorldBibleSpec.ManufacturingBomCode(product.SkuCode);

        await EnsureVersionAsync(
            organizationId,
            environmentId,
            product.SkuCode,
            WorldBibleSpec.VersionId(mbomCode, WorldBibleSpec.V1Revision),
            routingVersionId,
            WorldBibleSpec.V1EffectiveDate,
            product.IsHotSelling ? WorldBibleSpec.HotV1ValidTo : null,
            priority: 0,
            isDefault: !product.IsHotSelling,
            cancellationToken);

        if (!product.IsHotSelling)
        {
            return;
        }

        await EnsureVersionAsync(
            organizationId,
            environmentId,
            product.SkuCode,
            WorldBibleSpec.VersionId(mbomCode, WorldBibleSpec.V2Revision),
            routingVersionId,
            WorldBibleSpec.V2EffectiveDate,
            validTo: null,
            priority: 1,
            isDefault: true,
            cancellationToken);
    }

    private async Task EnsureVersionAsync(
        string organizationId,
        string environmentId,
        string skuCode,
        string mbomVersionId,
        string routingVersionId,
        DateOnly validFrom,
        DateOnly? validTo,
        int priority,
        bool isDefault,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ProductionVersions.AnyAsync(x =>
                x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.SkuCode == skuCode && x.MbomVersionId == mbomVersionId && x.RoutingVersionId == routingVersionId,
                cancellationToken))
        {
            return;
        }

        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            organizationId,
            environmentId,
            skuCode,
            mbomVersionId,
            routingVersionId,
            validFrom,
            validTo,
            lotSizeMin: null,
            lotSizeMax: null,
            priority,
            isDefault,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved world-bible engineering fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
