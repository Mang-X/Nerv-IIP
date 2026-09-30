using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class BusinessMesCommercialSourceReader(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessPlanningClient planning,
    IInternalServiceTokenProvider tokenProvider)
{
    public async Task<IReadOnlyDictionary<string, BusinessConsoleMesCommercialSourceFacts>> ReadAsync(
        string organizationId,
        string environmentId,
        string bearerToken,
        IReadOnlyCollection<(string WorkOrderId, BusinessConsoleMesSourcePlanReference? Source)> workOrders,
        CancellationToken cancellationToken)
    {
        var referencesByOrder = workOrders
            .Select(x => (x.WorkOrderId, References: References(x.Source)))
            .Where(x => x.References.Length > 0)
            .ToArray();
        if (referencesByOrder.Length == 0) return new Dictionary<string, BusinessConsoleMesCommercialSourceFacts>();

        var permission = await auth.CheckAsync(bearerToken,
            new BusinessGatewayPermissionRequirement(BusinessGatewayPermissions.PlanningDemandsRead,
                organizationId, environmentId, null, null),
            cancellationToken);
        if (!permission.IsAllowed)
            return referencesByOrder.ToDictionary(x => x.WorkOrderId,
                _ => new BusinessConsoleMesCommercialSourceFacts(BusinessConsoleMesCommercialSourceStatus.Forbidden, []), StringComparer.Ordinal);

        var sourceOrders = await Task.WhenAll(referencesByOrder.SelectMany(x => x.References)
            .Distinct(StringComparer.Ordinal).Select(ReadReferenceAsync));
        var ordersByReference = sourceOrders.ToDictionary(x => x.Reference, x => x.Orders, StringComparer.Ordinal);
        return referencesByOrder.ToDictionary(x => x.WorkOrderId,
            x => new BusinessConsoleMesCommercialSourceFacts(BusinessConsoleMesCommercialSourceStatus.Available,
                x.References.SelectMany(reference => ordersByReference[reference]).Distinct().ToArray()), StringComparer.Ordinal);

        async Task<(string Reference, BusinessConsoleMesSalesOrderLink[] Orders)> ReadReferenceAsync(string reference)
        {
            var orders = new List<BusinessConsoleMesSalesOrderLink>();
            var skip = 0;
            const int take = 100;
            while (true)
            {
                var page = await planning.ListDemandSourcesAsync(tokenProvider.BearerToken,
                    new BusinessConsoleDemandSourceListRequest(organizationId, environmentId, reference, skip, take), cancellationToken);
                orders.AddRange(page.Items
                    .Where(x => x.SourceReference == reference && x.DemandType == "sales-order")
                    .Select(x => new BusinessConsoleMesSalesOrderLink(x.SourceReference, x.SourceLineReference,
                        string.IsNullOrWhiteSpace(x.CustomerCode) ? null : x.CustomerCode)));
                if (page.Items.Count < take) break;
                skip += page.Items.Count;
            }
            return (reference, orders.Distinct().ToArray());
        }
    }

    private static string[] References(BusinessConsoleMesSourcePlanReference? source) =>
        source is null ? [] : (source.SourceDemandReferences ?? [])
            .Concat(source.SourceDemandReference is null ? [] : new[] { source.SourceDemandReference })
            .Distinct(StringComparer.Ordinal).ToArray();
}
