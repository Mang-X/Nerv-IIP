using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Numbering;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands;

public sealed record ErpNumberAllocation(string Number, bool IsIdempotentReplay);

public sealed class ErpNumberingService
{
    private readonly NumberingServiceCore _core;

    public ErpNumberingService(ApplicationDbContext? dbContext = null)
    {
        _core = new NumberingServiceCore(dbContext is null
            ? null
            : new EfCoreNumberingStore(dbContext, dbContext.NumberingCounters, dbContext.NumberingIdempotencyKeys));
    }

    public async Task<ErpNumberAllocation> AllocateAsync(
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
                "ERP"),
            cancellationToken);

        return new ErpNumberAllocation(allocation.Number, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return NumberingServiceCore.Fingerprint(parts);
    }
}
