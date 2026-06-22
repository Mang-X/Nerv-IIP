using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record GetBusinessPartnerCreditQuery(
    string OrganizationId,
    string EnvironmentId,
    string CustomerCode) : IQuery<BusinessPartnerCreditProfile>;

public sealed class GetBusinessPartnerCreditQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetBusinessPartnerCreditQuery, BusinessPartnerCreditProfile>
{
    public async Task<BusinessPartnerCreditProfile> Handle(GetBusinessPartnerCreditQuery request, CancellationToken cancellationToken)
    {
        var partner = await dbContext.BusinessPartners
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Code == request.CustomerCode &&
                !x.Disabled &&
                (x.PartnerType == "customer" || x.PartnerRoles.Contains("customer")),
                cancellationToken)
            ?? throw new KnownException($"Customer master data '{request.CustomerCode}' was not found or is not active.");

        if (!partner.CreditLimit.HasValue || string.IsNullOrWhiteSpace(partner.CreditCurrencyCode))
        {
            throw new KnownException($"Customer '{request.CustomerCode}' does not have a credit limit master-data profile.");
        }

        return new BusinessPartnerCreditProfile(
            partner.OrganizationId,
            partner.EnvironmentId,
            partner.Code,
            partner.CreditLimit.Value,
            partner.CreditCurrencyCode,
            partner.UpdatedAtUtc.ToString("O"));
    }
}
