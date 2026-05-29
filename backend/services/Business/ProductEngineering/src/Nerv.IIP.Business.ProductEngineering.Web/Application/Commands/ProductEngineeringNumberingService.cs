using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;

public sealed record ProductEngineeringNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class ProductEngineeringNumberingService
{
    private readonly NumberingServiceCore _core;

    public ProductEngineeringNumberingService()
    {
        _core = new NumberingServiceCore();
    }

    public ProductEngineeringNumberingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _core = new NumberingServiceCore(new EfCoreNumberingStore(
            dbContext,
            EfCoreNumberingStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public async Task<ProductEngineeringNumberAllocation> AllocateAsync(
        string organizationId,
        string environmentId,
        string documentType,
        string prefix,
        string? requestedNumber,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var allocation = await _core.AllocateAsync(
            new NumberingAllocationRequest(
                organizationId,
                environmentId,
                documentType,
                prefix,
                requestedNumber,
                idempotencyKey,
                payloadFingerprint,
                "ProductEngineering"),
            cancellationToken);

        return new ProductEngineeringNumberAllocation(allocation.Number, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return NumberingServiceCore.Fingerprint(parts);
    }
}
