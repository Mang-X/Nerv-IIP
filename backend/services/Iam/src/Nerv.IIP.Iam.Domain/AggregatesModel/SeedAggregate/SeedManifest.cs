using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;

public partial record SeedManifestId : IStringStronglyTypedId;

public class SeedManifest : Entity<SeedManifestId>, IAggregateRoot
{
    private SeedManifest()
    {
        Id = new SeedManifestId(string.Empty);
    }

    public SeedManifest(SeedManifestId id, string seedName, string seedVersion, string ownerService, DateTimeOffset appliedAtUtc)
    {
        Id = id;
        SeedName = seedName;
        SeedVersion = seedVersion;
        OwnerService = ownerService;
        AppliedAtUtc = appliedAtUtc;
    }

    public string SeedName { get; private set; } = string.Empty;
    public string SeedVersion { get; private set; } = string.Empty;
    public string OwnerService { get; private set; } = string.Empty;
    public DateTimeOffset AppliedAtUtc { get; private set; }
}
