using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;

public sealed record ProductEngineeringCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class ProductEngineeringCodingService
{
    private readonly CodeAllocator _allocator;

    public ProductEngineeringCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public ProductEngineeringCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public async Task<ProductEngineeringCodeAllocation> AllocateAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var allocation = await _allocator.AllocateAsync(
            new CodeAllocationRequest(
                organizationId,
                environmentId,
                StandardCodeRules.Get(ruleKey),
                fields,
                requestedCode,
                idempotencyKey,
                payloadFingerprint,
                "ProductEngineering"),
            cancellationToken);

        return new ProductEngineeringCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}