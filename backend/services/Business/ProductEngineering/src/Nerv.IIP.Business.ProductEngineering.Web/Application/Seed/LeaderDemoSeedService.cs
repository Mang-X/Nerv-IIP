using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public const string SkuCode = "SKU-DEMO-001";
    public const string RawMaterialSkuCode = "SKU-DEMO-RM-001";
    public const string MbomCode = "MBOM-DEMO-001";
    public const string RoutingCode = "ROUTING-DEMO-001";
    public const string Revision = "1";
    public const string MbomVersionId = MbomCode + ":" + Revision;
    public const string RoutingVersionId = RoutingCode + ":" + Revision;
    private static readonly DateOnly EffectiveDate = new(2026, 7, 1);

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var mbom = await dbContext.ManufacturingBoms
            .Include(x => x.MaterialLines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.BomCode == MbomCode && x.Revision == Revision, cancellationToken);
        if (mbom is null)
        {
            mbom = ManufacturingBom.CreateDraft(organizationId, environmentId, MbomCode, Revision, SkuCode)
                .AddMaterialLine(RawMaterialSkuCode, 2m, "pcs", 0m);
            mbom.ReleaseFromEngineeringBom("EBOM-DEMO-001:1", EngineeringVersionStatus.Published, EffectiveDate);
            dbContext.ManufacturingBoms.Add(mbom);
        }
        else if (mbom.SkuCode != SkuCode || mbom.Status != EngineeringVersionStatus.Published || mbom.EffectiveDate != EffectiveDate ||
                 mbom.MaterialLines.Count != 1 || mbom.MaterialLines.Single().SkuCode != RawMaterialSkuCode || mbom.MaterialLines.Single().Quantity != 2m)
        {
            throw Collision(MbomVersionId);
        }

        var routing = await dbContext.Routings
            .Include(x => x.Operations)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.RoutingCode == RoutingCode && x.Revision == Revision, cancellationToken);
        if (routing is null)
        {
            routing = Routing.CreateDraft(organizationId, environmentId, RoutingCode, Revision, SkuCode)
                .AddOperation(10, "WC-CNC-DEMO", "OP-CNC-DEMO", "Demo CNC operation", 5, 25, 0, "standard", true, true, false);
            routing.Release(EffectiveDate);
            dbContext.Routings.Add(routing);
        }
        else
        {
            var operation = routing.Operations.SingleOrDefault();
            if (routing.SkuCode != SkuCode || routing.Status != EngineeringVersionStatus.Published || routing.EffectiveDate != EffectiveDate ||
                operation is null || operation.Sequence != 10 || operation.WorkCenterCode != "WC-CNC-DEMO" ||
                operation.OperationCode != "OP-CNC-DEMO" || !operation.RequiresQualityInspection)
            {
                throw Collision(RoutingVersionId);
            }
        }

        var versions = await dbContext.ProductionVersions
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.SkuCode == SkuCode && x.Status == ProductionVersionStatus.Active)
            .ToArrayAsync(cancellationToken);
        if (versions.Length == 0)
        {
            dbContext.ProductionVersions.Add(ProductionVersion.Create(
                organizationId, environmentId, SkuCode, MbomVersionId, RoutingVersionId, EffectiveDate, null, null, null, 0, true,
                EngineeringVersionStatus.Published, EngineeringVersionStatus.Published));
        }
        else if (versions.Length != 1 || versions[0].MbomVersionId != MbomVersionId || versions[0].RoutingVersionId != RoutingVersionId ||
                 versions[0].ValidFrom != EffectiveDate || versions[0].ValidTo is not null || !versions[0].IsDefault)
        {
            throw Collision($"active production version for {SkuCode}");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo engineering fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
