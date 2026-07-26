using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;

public partial record WorkerId : IGuidStronglyTypedId;

/// <summary>
/// Factory worker master data. The aggregate carries the human readable employee number
/// (<see cref="Code"/>), the display name, and the stable person identifier
/// (<see cref="UserId"/>) that team membership, personnel skills, and MES dispatch all key on.
/// </summary>
public sealed class Worker : Entity<WorkerId>, IAggregateRoot
{
    /// <summary>Worker is on duty and can be dispatched.</summary>
    public const string StatusActive = "active";

    /// <summary>Worker is temporarily unavailable (leave, training, transfer).</summary>
    public const string StatusOnLeave = "on-leave";

    /// <summary>Worker left the factory; kept for historical dispatch traceability.</summary>
    public const string StatusResigned = "resigned";

    private Worker()
    {
    }

    private Worker(
        string organizationId,
        string environmentId,
        string code,
        string name,
        string userId,
        string? departmentCode,
        string? jobTitle,
        string employmentStatus,
        string? phone)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        UserId = Required(userId);
        DepartmentCode = Optional(departmentCode);
        JobTitle = Optional(jobTitle);
        EmploymentStatus = NormalizeStatus(employmentStatus);
        Phone = Optional(phone);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(Worker), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;

    /// <summary>Employee number shown to operators (for example EMP-001).</summary>
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Stable person identifier shared with team membership, skills, and MES dispatch.</summary>
    public string UserId { get; private set; } = string.Empty;
    public string? DepartmentCode { get; private set; }
    public string? JobTitle { get; private set; }
    public string EmploymentStatus { get; private set; } = StatusActive;
    public string? Phone { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>A worker can only be dispatched while enabled and on duty.</summary>
    public bool IsDispatchable => !Disabled && EmploymentStatus == StatusActive;

    public static Worker Create(
        string organizationId,
        string environmentId,
        string code,
        string name,
        string userId,
        string? departmentCode,
        string? jobTitle,
        string employmentStatus,
        string? phone)
    {
        return new Worker(organizationId, environmentId, code, name, userId, departmentCode, jobTitle, employmentStatus, phone);
    }

    public void Update(
        string name,
        string? departmentCode,
        string? jobTitle,
        string employmentStatus,
        string? phone)
    {
        EnsureEnabled();
        Name = Required(name);
        DepartmentCode = Optional(departmentCode);
        JobTitle = Optional(jobTitle);
        EmploymentStatus = NormalizeStatus(employmentStatus);
        Phone = Optional(phone);
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Worker), OrganizationId, EnvironmentId, Code));
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(Worker), OrganizationId, EnvironmentId, Code, validReason));
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
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(Worker), OrganizationId, EnvironmentId, Code));
    }

    public static string NormalizeStatus(string? employmentStatus)
    {
        if (string.IsNullOrWhiteSpace(employmentStatus))
        {
            return StatusActive;
        }

        var normalized = employmentStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            StatusActive or StatusOnLeave or StatusResigned => normalized,
            _ => throw new ArgumentException($"Unsupported employment status '{employmentStatus}'.", nameof(employmentStatus)),
        };
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled worker cannot be changed.");
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
