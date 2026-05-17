using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;

public partial record ManagedNodeId : IGuidStronglyTypedId;

public class ManagedNode : Entity<ManagedNodeId>, IAggregateRoot
{
    protected ManagedNode()
    {
    }

    public ManagedNode(string organizationId, string environmentId, string nodeKey, string nodeName, string deploymentKind)
    {
        Id = new ManagedNodeId(Guid.CreateVersion7());
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        NodeKey = nodeKey;
        NodeName = nodeName;
        DeploymentKind = deploymentKind;
        this.AddDomainEvent(new ManagedNodeRegisteredDomainEvent(organizationId, environmentId, nodeKey));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string NodeKey { get; private set; } = string.Empty;
    public string NodeName { get; private set; } = string.Empty;
    public string DeploymentKind { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);

    public void UpdateProfile(string nodeName, string deploymentKind)
    {
        NodeName = nodeName;
        DeploymentKind = deploymentKind;
    }
}
