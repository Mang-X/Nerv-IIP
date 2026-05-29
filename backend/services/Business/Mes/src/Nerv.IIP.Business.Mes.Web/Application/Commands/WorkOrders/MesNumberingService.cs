using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record MesNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class MesNumberingService
{
    private readonly NumberingServiceCore _core;

    public MesNumberingService(ApplicationDbContext? dbContext = null, IServiceScopeFactory? serviceScopeFactory = null)
    {
        _core = new NumberingServiceCore(dbContext is null
            ? null
            : new EfCoreNumberingStore(dbContext, CreateCounterDbContextLeaseFactory(serviceScopeFactory)));
    }

    public async Task<MesNumberAllocation> AllocateWorkOrderIdAsync(
        string organizationId,
        string environmentId,
        string? requestedWorkOrderId,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        return await AllocateAsync(organizationId, environmentId, "work-order", "WO", requestedWorkOrderId, idempotencyKey, payloadFingerprint, cancellationToken);
    }

    public async Task<MesNumberAllocation> AllocateAsync(
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
                "MES"),
            cancellationToken);

        return new MesNumberAllocation(allocation.Number, allocation.IsIdempotentReplay);
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
