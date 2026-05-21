using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;

public partial record ShiftId : IGuidStronglyTypedId;

public class Shift : Entity<ShiftId>, IAggregateRoot
{
    protected Shift()
    {
    }

    private Shift(string organizationId, string environmentId, string code, string name, TimeOnly startsAt, TimeOnly endsAt, int paidMinutes)
    {
        if (paidMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paidMinutes), "Paid minutes must be positive.");
        }

        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        StartsAt = startsAt;
        EndsAt = endsAt;
        CrossesMidnight = endsAt <= startsAt;
        PaidMinutes = paidMinutes;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public TimeOnly StartsAt { get; private set; }
    public TimeOnly EndsAt { get; private set; }
    public bool CrossesMidnight { get; private set; }
    public int PaidMinutes { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Shift Create(string organizationId, string environmentId, string code, string name, TimeOnly startsAt, TimeOnly endsAt, int paidMinutes)
    {
        return new Shift(organizationId, environmentId, code, name, startsAt, endsAt, paidMinutes);
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled shift cannot be changed.");
        }

        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code));
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
