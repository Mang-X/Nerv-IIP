using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record MasterDataNumberAllocation(string Code, bool IsIdempotentReplay);

public sealed class MasterDataNumberingService
{
    private readonly NumberingServiceCore _core;

    public MasterDataNumberingService(ApplicationDbContext? dbContext = null, IServiceScopeFactory? serviceScopeFactory = null)
    {
        _core = new NumberingServiceCore(dbContext is null
            ? null
            : new EfCoreNumberingStore(dbContext, CreateCounterDbContextLeaseFactory(serviceScopeFactory)));
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

    private static Func<CancellationToken, ValueTask<NumberingDbContextLease>> CreateCounterDbContextLeaseFactory(IServiceScopeFactory? serviceScopeFactory)
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        return _ =>
        {
            var scope = serviceScopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return ValueTask.FromResult(new NumberingDbContextLease(dbContext, scope));
        };
    }
}
