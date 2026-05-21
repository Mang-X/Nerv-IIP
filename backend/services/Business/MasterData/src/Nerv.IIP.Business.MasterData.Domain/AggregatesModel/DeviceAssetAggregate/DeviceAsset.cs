using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;

public partial record DeviceAssetId : IGuidStronglyTypedId;

public class DeviceAsset : Entity<DeviceAssetId>, IAggregateRoot
{
    private readonly List<string> controlSecretNames = [];

    protected DeviceAsset()
    {
    }

    private DeviceAsset(
        string organizationId,
        string environmentId,
        string code,
        string model,
        string lineCode,
        string workCenterCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Model = Required(model);
        LineCode = Required(lineCode);
        WorkCenterCode = Required(workCenterCode);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(DeviceAsset), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string LineCode { get; private set; } = string.Empty;
    public string WorkCenterCode { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<string> ControlSecretNames => controlSecretNames.AsReadOnly();

    public static DeviceAsset Register(
        string organizationId,
        string environmentId,
        string code,
        string model,
        string lineCode,
        string workCenterCode)
    {
        return new DeviceAsset(organizationId, environmentId, code, model, lineCode, workCenterCode);
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(DeviceAsset), OrganizationId, EnvironmentId, Code, validReason));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled device asset cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
