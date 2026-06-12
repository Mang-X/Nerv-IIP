using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class MasterDataCodingService
{
    private readonly CodeAllocator _allocator;

    public MasterDataCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public MasterDataCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public Task<MasterDataCodeAllocation> AllocateSkuCodeAsync(
        string organizationId,
        string environmentId,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        return AllocateAsync(
            organizationId,
            environmentId,
            "sku",
            requestedCode,
            idempotencyKey,
            payloadFingerprint,
            cancellationToken);
    }

    public async Task<MasterDataCodeAllocation> AllocateAsync(
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
                "MasterData"),
            cancellationToken);

        return new MasterDataCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}
