using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.StationAggregate;

public partial record StationId : IGuidStronglyTypedId;

/// <summary>
/// 工位（ISA-95 / GB/T 20720.1 的工作单元）。层级上级是产线，厂区与车间沿产线继承；
/// 工作中心只是产能与成本归集的可选关联，不是层级上级。
/// </summary>
public class Station : Entity<StationId>, IAggregateRoot
{
    protected Station()
    {
    }

    private Station(string organizationId, string environmentId, string code, string name, string lineCode, string? workCenterCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        LineCode = Required(lineCode);
        WorkCenterCode = Optional(workCenterCode);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Station), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Station), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LineCode { get; private set; } = string.Empty;
    public string? WorkCenterCode { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Station Create(string organizationId, string environmentId, string code, string name, string lineCode, string? workCenterCode = null)
    {
        return new Station(organizationId, environmentId, code, name, lineCode, workCenterCode);
    }

    public void Update(string name, string lineCode, string? workCenterCode)
    {
        EnsureEnabled();
        Name = Required(name);
        LineCode = Required(lineCode);
        WorkCenterCode = Optional(workCenterCode);
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Station), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Station), OrganizationId, EnvironmentId, Code));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        Touch();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Station), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Station), OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled station cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
