using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;

public partial record WorkCalendarId : IGuidStronglyTypedId;

public class WorkCalendar : Entity<WorkCalendarId>, IAggregateRoot
{
    private readonly List<WorkCalendarWorkingTime> workingTimes = [];
    private readonly List<WorkCalendarHoliday> holidays = [];
    private readonly List<WorkCalendarException> exceptions = [];

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
    public IReadOnlyCollection<WorkCalendarHoliday> Holidays => holidays.AsReadOnly();
    public IReadOnlyCollection<WorkCalendarException> Exceptions => exceptions.AsReadOnly();

    public static WorkCalendar Create(string organizationId, string environmentId, string code, string name)
    {
        return new WorkCalendar(organizationId, environmentId, code, name);
    }

    public void AddWorkingDay(DayOfWeek dayOfWeek)
    {
        EnsureEnabled();
        if (workingTimes.Any(x => x.DayOfWeek == dayOfWeek))
        {
            return;
        }

        workingTimes.Add(new WorkCalendarWorkingTime(dayOfWeek));
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(WorkCalendar), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new WorkCalendarChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public void Update(
        string name,
        IReadOnlyCollection<WorkCalendarWorkingTime>? newWorkingTimes,
        IReadOnlyCollection<WorkCalendarHoliday>? newHolidays,
        IReadOnlyCollection<WorkCalendarException>? newExceptions)
    {
        EnsureEnabled();
        Name = Required(name);
        if (newWorkingTimes is not null)
        {
            workingTimes.Clear();
            foreach (var item in newWorkingTimes.DistinctBy(x => x.DayOfWeek))
            {
                workingTimes.Add(item);
            }
        }

        if (newHolidays is not null)
        {
            holidays.Clear();
            holidays.AddRange(newHolidays.Select(x => new WorkCalendarHoliday(x.Date, Required(x.Name))));
        }

        if (newExceptions is not null)
        {
            exceptions.Clear();
            foreach (var item in newExceptions)
            {
                if (item.StartsAt.HasValue != item.EndsAt.HasValue)
                {
                    throw new ArgumentException("Exception working window requires both start and end.");
                }

                if (item.StartsAt.HasValue && item.EndsAt.HasValue)
                {
                    ValidateWindow(item.StartsAt.Value, item.EndsAt.Value, nameof(newExceptions));
                }

                exceptions.Add(new WorkCalendarException(
                    item.Date,
                    item.IsWorkingDay,
                    item.StartsAt,
                    item.EndsAt,
                    string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim()));
            }
        }

        TouchUpdated();
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
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(WorkCalendar), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new WorkCalendarChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        TouchUpdated();
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled work calendar cannot be changed.");
        }
    }

    private void TouchUpdated()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(WorkCalendar), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new WorkCalendarChangedDomainEvent(OrganizationId, EnvironmentId, Code));
    }

    private static void ValidateWindow(TimeOnly startsAt, TimeOnly endsAt, string parameterName)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Working time end must be after start.");
        }
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}

/// <summary>
/// Legacy "working time" name retained for API/table compatibility; this value marks a recurring working day.
/// </summary>
public record WorkCalendarWorkingTime(DayOfWeek DayOfWeek);
public record WorkCalendarHoliday(DateOnly Date, string Name);
public record WorkCalendarException(DateOnly Date, bool IsWorkingDay, TimeOnly? StartsAt, TimeOnly? EndsAt, string? Reason);
