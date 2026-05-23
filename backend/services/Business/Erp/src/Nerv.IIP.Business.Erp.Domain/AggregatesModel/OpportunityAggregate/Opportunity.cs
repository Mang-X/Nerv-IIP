using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;

public partial record OpportunityId : IGuidStronglyTypedId;

public sealed class Opportunity : Entity<OpportunityId>, IAggregateRoot
{
    private Opportunity()
    {
    }

    private Opportunity(string organizationId, string environmentId, string opportunityNo, string customerCode, string topic)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        OpportunityNo = ErpText.Required(opportunityNo, nameof(opportunityNo));
        CustomerCode = ErpText.Required(customerCode, nameof(customerCode));
        Topic = ErpText.Required(topic, nameof(topic));
        Status = "open";
        OpenedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OpportunityNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string Topic { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTime OpenedAtUtc { get; private set; }

    public static Opportunity Open(string organizationId, string environmentId, string opportunityNo, string customerCode, string topic)
    {
        return new Opportunity(organizationId, environmentId, opportunityNo, customerCode, topic);
    }
}
