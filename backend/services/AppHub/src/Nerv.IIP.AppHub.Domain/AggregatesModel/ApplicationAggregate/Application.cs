using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;

public partial record ApplicationId : IGuidStronglyTypedId;
public partial record ApplicationVersionId : IGuidStronglyTypedId;

public class Application : Entity<Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.ApplicationId>, IAggregateRoot
{
    protected Application()
    {
    }

    public Application(string organizationId, string environmentId, string applicationKey, string applicationName, string version)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ApplicationKey = applicationKey;
        ApplicationName = applicationName;
        AddVersion(version);
        this.AddDomainEvent(new ApplicationRegisteredDomainEvent(organizationId, environmentId, applicationKey, version));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ApplicationKey { get; private set; } = string.Empty;
    public string ApplicationName { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new();
    public RowVersion RowVersion { get; private set; } = new(0);
    public ICollection<ApplicationVersion> Versions { get; private set; } = [];

    public void RenameAndTrackVersion(string applicationName, string version)
    {
        ApplicationName = applicationName;
        AddVersion(version);
    }

    public void Deactivate()
    {
        if (Deleted.Value)
        {
            return;
        }

        Deleted = new Deleted(true);
        this.AddDomainEvent(new ApplicationDeactivatedDomainEvent(OrganizationId, EnvironmentId, ApplicationKey));
    }

    private void AddVersion(string version)
    {
        if (Versions.Any(x => x.Version == version))
        {
            return;
        }

        Versions.Add(new ApplicationVersion(version));
    }
}

public class ApplicationVersion : Entity<Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.ApplicationVersionId>
{
    protected ApplicationVersion()
    {
    }

    public ApplicationVersion(string version)
    {
        Version = version;
    }

    public ApplicationId ApplicationId { get; private set; } = null!;
    public string Version { get; private set; } = string.Empty;
}
