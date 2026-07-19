namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.IntegrationEvents;

public sealed class SalesOrderDemandProjection
{
    private SalesOrderDemandProjection()
    {
    }

    public SalesOrderDemandProjection(string organizationId, string environmentId, string salesOrderId, string salesOrderNo, string customerCode, string siteCode, int orderVersion, string status, string lastEventId, DateTimeOffset occurredAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        SalesOrderId = salesOrderId;
        Apply(salesOrderNo, customerCode, siteCode, orderVersion, status, lastEventId, occurredAtUtc);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SalesOrderId { get; private set; } = string.Empty;
    public string SalesOrderNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public int OrderVersion { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string LastEventId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public bool Apply(string salesOrderNo, string customerCode, string siteCode, int orderVersion, string status, string lastEventId, DateTimeOffset occurredAtUtc)
    {
        if (orderVersion <= OrderVersion)
        {
            return false;
        }

        SalesOrderNo = salesOrderNo;
        CustomerCode = customerCode;
        SiteCode = siteCode;
        OrderVersion = orderVersion;
        Status = status;
        LastEventId = lastEventId;
        OccurredAtUtc = occurredAtUtc;
        return true;
    }
}
