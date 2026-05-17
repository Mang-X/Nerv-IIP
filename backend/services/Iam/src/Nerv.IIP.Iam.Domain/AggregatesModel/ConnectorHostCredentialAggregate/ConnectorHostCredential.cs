using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;

public partial record ConnectorHostCredentialId : IStringStronglyTypedId;
public partial record ConnectorHostCredentialCapabilityId : IStringStronglyTypedId;

public class ConnectorHostCredential : Entity<ConnectorHostCredentialId>, IAggregateRoot
{
    private readonly List<ConnectorHostCredentialCapability> capabilities = [];

    private ConnectorHostCredential()
    {
        Id = new ConnectorHostCredentialId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
        EnvironmentId = new IamEnvironmentId(string.Empty);
    }

    public ConnectorHostCredential(
        ConnectorHostCredentialId id,
        string connectorHostId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string secretHash,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validToUtc,
        IEnumerable<string> capabilityScope)
    {
        Id = id;
        ConnectorHostId = connectorHostId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        SecretHash = secretHash;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        ReplaceCapabilities(capabilityScope);
    }

    public string ConnectorHostId { get; private set; } = string.Empty;
    public OrganizationId OrganizationId { get; private set; }
    public IamEnvironmentId EnvironmentId { get; private set; }
    public string SecretHash { get; private set; } = string.Empty;
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidToUtc { get; private set; }
    public IReadOnlyCollection<ConnectorHostCredentialCapability> Capabilities => capabilities;

    public bool IsValidAt(DateTimeOffset now)
    {
        return ValidFromUtc <= now && (ValidToUtc is null || ValidToUtc > now);
    }

    public void ReplaceSecretHash(string secretHash)
    {
        SecretHash = secretHash;
    }

    public void ReplaceCapabilities(IEnumerable<string> capabilityScope)
    {
        var desiredCapabilities = capabilityScope.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        capabilities.RemoveAll(x => !desiredCapabilities.Contains(x.CapabilityCode));

        var existingCapabilities = capabilities.Select(x => x.CapabilityCode).ToHashSet(StringComparer.Ordinal);
        foreach (var capability in desiredCapabilities.Order(StringComparer.Ordinal))
        {
            if (!existingCapabilities.Contains(capability))
            {
                capabilities.Add(new ConnectorHostCredentialCapability(
                    new ConnectorHostCredentialCapabilityId($"{Id.Id}:{capability}"),
                    Id,
                    capability));
            }
        }
    }
}

public class ConnectorHostCredentialCapability : Entity<ConnectorHostCredentialCapabilityId>
{
    private ConnectorHostCredentialCapability()
    {
        Id = new ConnectorHostCredentialCapabilityId(string.Empty);
        ConnectorHostCredentialId = new ConnectorHostCredentialId(string.Empty);
    }

    internal ConnectorHostCredentialCapability(
        ConnectorHostCredentialCapabilityId id,
        ConnectorHostCredentialId connectorHostCredentialId,
        string capabilityCode)
    {
        Id = id;
        ConnectorHostCredentialId = connectorHostCredentialId;
        CapabilityCode = capabilityCode;
    }

    public ConnectorHostCredentialId ConnectorHostCredentialId { get; private set; }
    public string CapabilityCode { get; private set; } = string.Empty;
}
