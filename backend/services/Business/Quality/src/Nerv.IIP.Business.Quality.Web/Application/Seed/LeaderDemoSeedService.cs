using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public const string PlanCode = "IP-DEMO-OP-001";

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var plan = await dbContext.InspectionPlans
            .Include(x => x.Characteristics)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.PlanCode == PlanCode, cancellationToken);
        if (plan is null)
        {
            plan = InspectionPlan.Create(
                organizationId, environmentId, PlanCode, "operation", "SKU-DEMO-001", null, "WC-CNC-DEMO", null, null);
            plan.AddCharacteristic(
                "diameter", "Demo diameter", "caliper", "major", true, "each",
                InspectionCharacteristicTypes.Variable, 50m, 49.5m, 50.5m, "mm", null);
            plan.Activate();
            dbContext.InspectionPlans.Add(plan);
        }
        else
        {
            var characteristic = plan.Characteristics.SingleOrDefault();
            if (plan.Status != "active" || plan.Category != "operation" || plan.SkuCode != "SKU-DEMO-001" || plan.WorkCenterId != "WC-CNC-DEMO" ||
                characteristic is null || characteristic.CharacteristicCode != "diameter" || characteristic.CharacteristicType != InspectionCharacteristicTypes.Variable ||
                characteristic.NominalValue != 50m || characteristic.LowerSpecLimit != 49.5m || characteristic.UpperSpecLimit != 50.5m)
            {
                throw new InvalidOperationException($"Reserved leader-demo quality plan '{PlanCode}' exists with incompatible tenant facts; the seed will not overwrite it.");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
