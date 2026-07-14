using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands;

public sealed record ErpCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class ErpCodingService
{
    private readonly CodeAllocator _allocator;

    public ErpCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public ErpCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public async Task<ErpCodeAllocation> AllocateAsync(
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
                "ERP"),
            cancellationToken);

        return new ErpCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public async Task<ErpCodeAllocation?> TryPeekReplayAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var replay = await _allocator.TryPeekReplayAsync(
            new CodeAllocationRequest(
                organizationId,
                environmentId,
                StandardCodeRules.Get(ruleKey),
                Fields: null,
                RequestedCode: null,
                idempotencyKey,
                payloadFingerprint,
                "ERP"),
            cancellationToken);
        return replay is null ? null : new ErpCodeAllocation(replay.Code, replay.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}
