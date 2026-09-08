using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using static Nerv.IIP.Business.ProductEngineering.Web.Application.Seed.WalkthroughSeedSpec;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>人工走查活塞杆的结构性工程输入；不产生工单或库存。</summary>
internal sealed class WalkthroughRodSeedService(ApplicationDbContext dbContext)
{
    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        const string revision = "1";
        var effectiveDate = WorldBibleSpec.V1EffectiveDate;
        var ebomCode = WorldBibleSpec.EngineeringBomCode(RodSkuCode);
        var mbomCode = WorldBibleSpec.ManufacturingBomCode(RodSkuCode);
        var routingCode = WorldBibleSpec.RoutingCode(RodSkuCode);
        var ebomVersionId = WorldBibleSpec.VersionId(ebomCode, revision);
        var mbomVersionId = WorldBibleSpec.VersionId(mbomCode, revision);
        var routingVersionId = WorldBibleSpec.VersionId(routingCode, revision);

        var item = await dbContext.EngineeringItems.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
            x.ItemCode == RodSkuCode && x.Revision == revision, cancellationToken);
        if (item is null)
        {
            dbContext.EngineeringItems.Add(EngineeringItem.CreateRevision(
                organizationId, environmentId, RodSkuCode, revision, RodName, release: true));
        }
        else if (item.Name != RodName || item.Status != EngineeringVersionStatus.Published)
        {
            throw Collision(RodSkuCode);
        }

        var ebom = await dbContext.EngineeringBoms.Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
            x.BomCode == ebomCode && x.Revision == revision, cancellationToken);
        if (ebom is null)
        {
            ebom = EngineeringBom.CreateDraft(organizationId, environmentId, ebomCode, revision, RodSkuCode)
                .AddLine(RodRawMaterialSkuCode, RodRawMaterialKilogramsPerPiece, "kg");
            ebom.Release(effectiveDate);
            dbContext.EngineeringBoms.Add(ebom);
        }
        else if (ebom.ParentItemCode != RodSkuCode || ebom.Status != EngineeringVersionStatus.Published ||
            ebom.EffectiveDate != effectiveDate || ebom.Lines.Count != 1 ||
            ebom.Lines.Any(x => x.ChildItemCode != RodRawMaterialSkuCode ||
                x.Quantity != RodRawMaterialKilogramsPerPiece || x.UnitOfMeasureCode != "kg" ||
                x.ScrapRate != 0m || x.YieldRate != 1m || x.IsPhantom || x.Backflush ||
                x.AlternateGroup != null || x.AlternatePriority != null || x.ReferenceDesignators != null))
        {
            throw Collision(ebomVersionId);
        }

        var mbom = await dbContext.ManufacturingBoms.Include(x => x.MaterialLines).Include(x => x.RecipeLines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.BomCode == mbomCode && x.Revision == revision, cancellationToken);
        if (mbom is null)
        {
            mbom = ManufacturingBom.CreateDraft(organizationId, environmentId, mbomCode, revision, RodSkuCode)
                .AddMaterialLine(RodRawMaterialSkuCode, RodRawMaterialKilogramsPerPiece, "kg", 0m);
            mbom.ReleaseFromEngineeringBom(ebomVersionId, EngineeringVersionStatus.Published, effectiveDate);
            dbContext.ManufacturingBoms.Add(mbom);
        }
        else if (mbom.SkuCode != RodSkuCode || mbom.Status != EngineeringVersionStatus.Published ||
            mbom.EngineeringBomVersionId != ebomVersionId || mbom.EffectiveDate != effectiveDate ||
            mbom.MaterialLines.Count != 1 || mbom.RecipeLines.Count != 0 ||
            mbom.MaterialLines.Any(x => x.SkuCode != RodRawMaterialSkuCode ||
                x.Quantity != RodRawMaterialKilogramsPerPiece || x.UnitOfMeasureCode != "kg" ||
                x.ScrapRate != 0m || x.YieldRate != 1m || x.IsPhantom || x.Backflush ||
                x.AlternateGroup != null || x.AlternatePriority != null ||
                x.SubstituteSkuCodes != null || x.ReferenceDesignators != null))
        {
            throw Collision(mbomVersionId);
        }

        var routing = await dbContext.Routings.Include(x => x.Operations).SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
            x.RoutingCode == routingCode && x.Revision == revision, cancellationToken);
        if (routing is null)
        {
            routing = Routing.CreateDraft(organizationId, environmentId, routingCode, revision, RodSkuCode);
            foreach (var operation in RodOperations)
            {
                routing.AddOperation(operation.Sequence, operation.DefaultWorkCenterCode,
                    operation.OperationCode, operation.OperationName, operation.SetupMinutes,
                    operation.RunMinutes, operation.TeardownMinutes, "standard", true,
                    operation.RequiresQualityInspection, false);
            }
            routing.Release(effectiveDate);
            dbContext.Routings.Add(routing);
        }
        else if (routing.SkuCode != RodSkuCode || routing.Status != EngineeringVersionStatus.Published ||
            routing.EffectiveDate != effectiveDate || routing.Operations.Count != RodOperations.Count ||
            routing.Operations.OrderBy(x => x.Sequence).Zip(RodOperations).Any(pair =>
                pair.First.Sequence != pair.Second.Sequence || pair.First.OperationCode != pair.Second.OperationCode ||
                pair.First.WorkCenterCode != pair.Second.DefaultWorkCenterCode ||
                pair.First.OperationName != pair.Second.OperationName ||
                pair.First.SetupMinutes != pair.Second.SetupMinutes || pair.First.RunMinutes != pair.Second.RunMinutes ||
                pair.First.TeardownMinutes != pair.Second.TeardownMinutes || pair.First.ControlKey != "standard" ||
                !pair.First.RequiresReporting || pair.First.RequiresQualityInspection != pair.Second.RequiresQualityInspection ||
                pair.First.IsOutsourced || pair.First.RequiredSkillCode != null))
        {
            throw Collision(routingVersionId);
        }

        var version = await dbContext.ProductionVersions.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.SkuCode == RodSkuCode &&
            x.MbomVersionId == mbomVersionId && x.RoutingVersionId == routingVersionId, cancellationToken);
        if (version is null)
        {
            dbContext.ProductionVersions.Add(ProductionVersion.Create(organizationId, environmentId, RodSkuCode,
                mbomVersionId, routingVersionId, effectiveDate, null, null, null, 0, true,
                EngineeringVersionStatus.Published, EngineeringVersionStatus.Published));
        }
        else if (version.Status != ProductionVersionStatus.Active || version.ValidFrom != effectiveDate ||
            version.ValidTo != null || version.LotSizeMin != null || version.LotSizeMax != null ||
            version.Priority != 0 || !version.IsDefault)
        {
            throw Collision(mbomVersionId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved walkthrough engineering fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
