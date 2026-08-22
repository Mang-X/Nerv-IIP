using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.MasterData;

namespace Nerv.IIP.Business.Erp.Web.Application.MasterData;

public static class BusinessPartnerAvailabilityGate
{
    public static async Task EnsureActiveAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string partnerCode,
        CancellationToken cancellationToken)
    {
        var isDisabled = await dbContext.BusinessPartnerAvailabilities.AnyAsync(x =>
            x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId
            && x.PartnerCode == partnerCode
            && x.Status == BusinessPartnerAvailabilityStatuses.Disabled,
            cancellationToken);

        if (isDisabled)
        {
            throw new KnownException($"业务伙伴『{partnerCode}』已停用，不能用于新订单。");
        }
    }
}
