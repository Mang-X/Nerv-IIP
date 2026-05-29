using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;

public sealed record ProductEngineeringNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class ProductEngineeringNumberingService
{
    private readonly NumberingServiceCore _core;

    public ProductEngineeringNumberingService(ApplicationDbContext? dbContext = null, IServiceScopeFactory? serviceScopeFactory = null)
    {
        _core = new NumberingServiceCore(dbContext is null
            ? null
            : new EfCoreNumberingStore(dbContext, CreateCounterDbContextLeaseFactory(serviceScopeFactory)));
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
