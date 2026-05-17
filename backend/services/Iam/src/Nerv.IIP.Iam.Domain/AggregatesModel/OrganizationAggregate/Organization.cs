using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;

public partial record OrganizationId : IStringStronglyTypedId;
public partial record IamEnvironmentId : IStringStronglyTypedId;

public class Organization : Entity<OrganizationId>, IAggregateRoot
{
    private Organization()
    {
        Id = new OrganizationId(string.Empty);
    }

    public Organization(OrganizationId id, string name, string status)
    {
        Id = id;
        Name = name;
        Status = status;
    }

    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
}

public class IamEnvironment : Entity<IamEnvironmentId>, IAggregateRoot
{
    private IamEnvironment()
    {
        Id = new IamEnvironmentId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
    }

    public IamEnvironment(IamEnvironmentId id, OrganizationId organizationId, string name, string status)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Status = status;
    }

    public OrganizationId OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
}
