using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record DemandPlanningCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class DemandPlanningCodingService
{
    private readonly CodeAllocator _allocator;

    public DemandPlanningCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public DemandPlanningCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public Task<DemandPlanningCodeAllocation> AllocateDemandReferenceAsync(
        string organizationId,
        string environmentId,
        string? requestedReference,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        return AllocateAsync(
            organizationId,
            environmentId,
            "demand",
            requestedReference,
            idempotencyKey,
            payloadFingerprint,
            cancellationToken);
    }

    public async Task<DemandPlanningCodeAllocation> AllocateAsync(
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
                "DemandPlanning"),
            cancellationToken);

        return new DemandPlanningCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}
