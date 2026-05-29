using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataNumberAllocation(string Code, bool IsIdempotentReplay);

public sealed class MasterDataNumberingService
{
    private readonly NumberingServiceCore _core;

    public MasterDataNumberingService()
    {
        _core = new NumberingServiceCore();
    }

    public MasterDataNumberingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _core = new NumberingServiceCore(new EfCoreNumberingStore(
            dbContext,
            EfCoreNumberingStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public async Task<MasterDataNumberAllocation> AllocateSkuCodeAsync(
        string organizationId,
        string environmentId,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var allocation = await _core.AllocateAsync(
            new NumberingAllocationRequest(
                organizationId,
                environmentId,
                "sku",
                "SKU",
                requestedCode,
                idempotencyKey,
                payloadFingerprint,
                "sku"),
            cancellationToken);

        return new MasterDataNumberAllocation(allocation.Number, allocation.IsIdempotentReplay);
    }
}
