using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Nerv.IIP.Business.Mes.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record MesCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class MesCodingService
{
    private readonly CodeAllocator _allocator;

    public MesCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public MesCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public Task<MesCodeAllocation> AllocateWorkOrderIdAsync(
        string organizationId,
        string environmentId,
        string? requestedWorkOrderId,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        return AllocateAsync(
            organizationId,
            environmentId,
            "work-order",
            requestedWorkOrderId,
            idempotencyKey,
            payloadFingerprint,
            cancellationToken);
    }

    public async Task<MesCodeAllocation> AllocateAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        CodeAllocation allocation;
        try
        {
            allocation = await _allocator.AllocateAsync(
                new CodeAllocationRequest(
                    organizationId,
                    environmentId,
                    StandardCodeRules.Get(ruleKey),
                    fields,
                    requestedCode,
                    idempotencyKey,
                    payloadFingerprint,
                    "MES"),
                cancellationToken);
        }
        catch (KnownException exception) when (
            exception.Message.Contains("conflicts with a different", StringComparison.Ordinal))
        {
            throw new Nerv.IIP.Business.Mes.Web.Application.Errors.MesIdempotencyConflictException();
        }

        return new MesCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}
