using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Nerv.IIP.Business.Erp.Infrastructure;
using NetCorePal.Extensions.Primitives;

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

    public static async Task<ErpCodeAllocation?> FindPersistedReplayAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string ruleKey,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedKey is null)
        {
            return null;
        }

        var record = await dbContext.CodeIdempotencyKeys.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.RuleKey == ruleKey
            && x.IdempotencyKey == normalizedKey,
            cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
        {
            throw new KnownException($"Idempotency key '{normalizedKey}' has already been used with different ERP create data.");
        }

        return new ErpCodeAllocation(record.Code, true);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}
