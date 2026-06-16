using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;

public partial record ShiftId : IGuidStronglyTypedId;

public class Shift : Entity<ShiftId>, IAggregateRoot
{
    protected Shift()
    {
    }

    private Shift(string organizationId, string environmentId, string code, string name, TimeOnly startsAt, TimeOnly endsAt, int paidMinutes, int breakMinutes)
    {
        ValidateMinutes(paidMinutes, breakMinutes);
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        StartsAt = startsAt;
        EndsAt = endsAt;
        CrossesMidnight = endsAt <= startsAt;
        PaidMinutes = paidMinutes;
        BreakMinutes = breakMinutes;
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
    public int BreakMinutes { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Shift Create(string organizationId, string environmentId, string code, string name, TimeOnly startsAt, TimeOnly endsAt, int paidMinutes, int breakMinutes = 0)
    {
        return new Shift(organizationId, environmentId, code, name, startsAt, endsAt, paidMinutes, breakMinutes);
    }

    public void Update(string name, TimeOnly startsAt, TimeOnly endsAt, int paidMinutes, int? breakMinutes = null)
    {
        var nextBreakMinutes = breakMinutes ?? BreakMinutes;
        ValidateMinutes(paidMinutes, nextBreakMinutes);
        EnsureEnabled();
        Name = Required(name);
        StartsAt = startsAt;
        EndsAt = endsAt;
        CrossesMidnight = endsAt <= startsAt;
        PaidMinutes = paidMinutes;
        BreakMinutes = nextBreakMinutes;
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        if (Disabled)
        {
            return;
        }

        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code));
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
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(Shift), OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled shift cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static void ValidateMinutes(int paidMinutes, int breakMinutes)
    {
        if (paidMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paidMinutes), "Paid minutes must be positive.");
        }

        if (breakMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(breakMinutes), "Break minutes cannot be negative.");
        }
    }
}
