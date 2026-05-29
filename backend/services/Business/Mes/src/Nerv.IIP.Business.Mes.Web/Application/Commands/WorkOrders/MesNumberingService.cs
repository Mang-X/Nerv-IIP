using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record MesNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class MesNumberingService
{
    private readonly NumberingServiceCore _core;

    public MesNumberingService()
    {
        _core = new NumberingServiceCore();
    }

    public MesNumberingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _core = new NumberingServiceCore(new EfCoreNumberingStore(
            dbContext,
            EfCoreNumberingStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
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
}
