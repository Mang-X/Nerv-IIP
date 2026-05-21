using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;

public partial record WorkCenterId : IGuidStronglyTypedId;

public class WorkCenter : Entity<WorkCenterId>, IAggregateRoot
{
    protected WorkCenter()
    {
    }

    private WorkCenter(string organizationId, string environmentId, string code, string name, int capacityMinutesPerDay)
    {
        if (capacityMinutesPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityMinutesPerDay), "Capacity minutes per day must be positive.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        CapacityMinutesPerDay = capacityMinutesPerDay;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int CapacityMinutesPerDay { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static WorkCenter Create(string organizationId, string environmentId, string code, string name, int capacityMinutesPerDay)
    {
        return new WorkCenter(organizationId, environmentId, code, name, capacityMinutesPerDay);
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code, validReason));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled work center cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
