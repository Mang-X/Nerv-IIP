using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record DemandPlanningNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class DemandPlanningNumberingService
{
    private readonly NumberingServiceCore _core;

    public DemandPlanningNumberingService()
    {
        _core = new NumberingServiceCore();
    }

    public DemandPlanningNumberingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _core = new NumberingServiceCore(new EfCoreNumberingStore(
            dbContext,
            EfCoreNumberingStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public async Task<DemandPlanningNumberAllocation> AllocateDemandReferenceAsync(
        string organizationId,
        string environmentId,
        string? requestedReference,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var allocation = await _core.AllocateAsync(
            new NumberingAllocationRequest(
                organizationId,
                environmentId,
                "demand",
                "DEMAND",
                requestedReference,
                idempotencyKey,
                payloadFingerprint,
                "DemandPlanning"),
            cancellationToken);

        return new DemandPlanningNumberAllocation(allocation.Number, allocation.IsIdempotentReplay);
    }
}
