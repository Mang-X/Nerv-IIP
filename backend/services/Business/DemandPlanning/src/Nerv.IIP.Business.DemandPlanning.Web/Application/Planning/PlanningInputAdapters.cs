using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public interface IPlanningInputSnapshotProvider
{
    Task<PlanningInputSnapshotResult> GetSnapshotAsync(
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        CancellationToken cancellationToken);
}

public sealed record PlanningInputSnapshotResult(
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource,
    IReadOnlyCollection<DemandSnapshot> Demands,
    IReadOnlyCollection<InventoryAvailabilitySnapshot> Availability,
    IReadOnlyCollection<ProductionVersionSnapshot> ProductionVersions,
    IReadOnlyCollection<BomComponentSnapshot> BomComponents);

public sealed class DemandPlanningFixtureInputSnapshotProvider(ApplicationDbContext dbContext) : IPlanningInputSnapshotProvider
{
    public async Task<PlanningInputSnapshotResult> GetSnapshotAsync(
        string organizationId,
        string environmentId,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        CancellationToken cancellationToken)
    {
        var demands = await dbContext.DemandSources
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.DueDate >= horizonStart
                && x.DueDate <= horizonEnd)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.SourceReference)
            .Select(x => new DemandSnapshot(x.SourceReference, x.SkuCode, x.UomCode, x.SiteCode, x.Quantity, x.DueDate))
            .ToListAsync(cancellationToken);

        return new PlanningInputSnapshotResult(
            "fixture-production-engineering-snapshot",
            "fixture-inventory-availability-snapshot",
            demands,
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
            ],
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001"),
            ],
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m),
            ]);
    }
}
