using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;

public partial record WorkCalendarId : IGuidStronglyTypedId;

public class WorkCalendar : Entity<WorkCalendarId>, IAggregateRoot
{
    private readonly List<WorkCalendarWorkingTime> workingTimes = [];

    protected WorkCalendar()
    {
    }

    private WorkCalendar(string organizationId, string environmentId, string code, string name)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(WorkCalendar), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new WorkCalendarChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<WorkCalendarWorkingTime> WorkingTimes => workingTimes.AsReadOnly();

    public static WorkCalendar Create(string organizationId, string environmentId, string code, string name)
    {
        return new WorkCalendar(organizationId, environmentId, code, name);
    }

    public void AddWorkingTime(DayOfWeek dayOfWeek, TimeOnly startsAt, TimeOnly endsAt)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), "Working time end must be after start.");
        }

        EnsureEnabled();
        workingTimes.Add(new WorkCalendarWorkingTime(dayOfWeek, startsAt, endsAt));
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(WorkCalendar), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new WorkCalendarChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(WorkCalendar), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new WorkCalendarChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled work calendar cannot be changed.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}

public record WorkCalendarWorkingTime(DayOfWeek DayOfWeek, TimeOnly StartsAt, TimeOnly EndsAt);
