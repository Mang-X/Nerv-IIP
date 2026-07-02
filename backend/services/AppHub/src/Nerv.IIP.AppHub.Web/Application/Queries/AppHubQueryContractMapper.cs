using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.AppHub.Web.Application.Queries;

internal static class AppHubQueryContractMapper
{
    public static InstanceListCriteria ToDomainCriteria(this InstanceListQuery query)
    {
        return new InstanceListCriteria(
            query.OrganizationId,
            query.EnvironmentId,
            query.PageIndex,
            query.PageSize,
            query.SortBy,
            query.SortOrder,
            query.FilterSearch);
    }

    public static InstanceListResponse ToContract(this InstanceListResult result)
    {
        return new InstanceListResponse(
            result.EffectivePageIndex,
            result.EffectivePageSize,
            result.TotalCount,
            result.Items.Select(ToContract).ToList());
    }

    public static InstanceDetailResponse ToContract(this InstanceDetailFact detail)
    {
        return new InstanceDetailResponse(
            detail.ApplicationKey,
            detail.ApplicationName,
            detail.Version,
            detail.NodeKey,
            detail.NodeName,
            detail.InstanceKey,
            detail.InstanceName,
            detail.ReportedStatus,
            detail.HealthStatus,
            detail.LastHeartbeatAtUtc,
            detail.LastStateAtUtc,
            detail.Capabilities.Select(ToContract).ToList(),
            detail.Metadata);
    }

    private static InstanceListItem ToContract(InstanceListItemFact item)
    {
        return new InstanceListItem(
            item.ApplicationKey,
            item.ApplicationName,
            item.Version,
            item.NodeKey,
            item.NodeName,
            item.InstanceKey,
            item.InstanceName,
            item.ReportedStatus,
            item.HealthStatus,
            item.LastHeartbeatAtUtc,
            item.LastStateAtUtc);
    }

    private static CapabilitySummary ToContract(CapabilitySummaryFact capability)
    {
        return new CapabilitySummary(
            capability.CapabilityCode,
            capability.CapabilityVersion,
            capability.Category,
            capability.SupportedOperations);
    }
}
