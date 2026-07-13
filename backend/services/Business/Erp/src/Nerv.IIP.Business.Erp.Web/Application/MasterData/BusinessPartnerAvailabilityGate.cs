using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

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
            && x.Status == "disabled",
            cancellationToken);

        if (isDisabled)
        {
            throw new KnownException($"Business partner '{partnerCode}' is disabled and cannot be used for a new order.");
        }
    }
}
