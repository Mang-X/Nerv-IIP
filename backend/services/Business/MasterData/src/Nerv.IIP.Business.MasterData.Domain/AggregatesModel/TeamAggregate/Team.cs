using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;

public partial record TeamId : IGuidStronglyTypedId;

public class Team : Entity<TeamId>, IAggregateRoot
{
    protected Team()
    {
    }

    private Team(string organizationId, string environmentId, string code, string name, string departmentCode, string shiftCode, string? workshopCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        DepartmentCode = Required(departmentCode);
        ShiftCode = Required(shiftCode);
        WorkshopCode = Optional(workshopCode);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Team), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string DepartmentCode { get; private set; } = string.Empty;
    public string ShiftCode { get; private set; } = string.Empty;

    /// <summary>
    /// Optional workshop the team staffs. Teams are workshop-level in practice (one shift crew
    /// covers every work center in its workshop), so MES dispatch resolves candidates as
    /// work center -> its workshop -> the teams of that workshop -> their members.
    /// </summary>
    public string? WorkshopCode { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Team Create(string organizationId, string environmentId, string code, string name, string departmentCode, string shiftCode, string? workshopCode = null)
    {
        return new Team(organizationId, environmentId, code, name, departmentCode, shiftCode, workshopCode);
    }

    public void Update(string name, string departmentCode, string shiftCode, string? workshopCode)
    {
        EnsureEnabled();
        Name = Required(name);
        DepartmentCode = Required(departmentCode);
        ShiftCode = Required(shiftCode);
        WorkshopCode = Optional(workshopCode);
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Team), OrganizationId, EnvironmentId, Code));
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Team), OrganizationId, EnvironmentId, Code, validReason));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Team), OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled team cannot be changed.");
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
