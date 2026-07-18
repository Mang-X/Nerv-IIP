using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;

namespace Nerv.IIP.Business.Mes.Web.Application.MasterData;

public sealed class DisabledMesSkuException(string skuCode)
    : KnownException($"SKU '{skuCode}' is disabled and cannot be used for a new MES work order.");

public static class MesSkuAvailabilityGate
{
    public static async Task EnsureActiveAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string skuCode,
        CancellationToken cancellationToken)
    {
        if (await IsDisabledAsync(
                dbContext,
                organizationId,
                environmentId,
                skuCode,
                cancellationToken))
        {
            throw new DisabledMesSkuException(skuCode);
        }
    }

    public static Task<bool> IsDisabledAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string skuCode,
        CancellationToken cancellationToken) =>
        dbContext.MesSkuAvailabilities.AnyAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.SkuCode == skuCode
            && x.Status == MesSkuAvailabilityStatuses.Disabled,
            cancellationToken);
}
