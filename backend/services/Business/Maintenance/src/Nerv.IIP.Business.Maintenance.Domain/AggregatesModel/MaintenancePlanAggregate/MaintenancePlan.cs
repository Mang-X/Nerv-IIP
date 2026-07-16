using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;

public partial record MaintenancePlanId : IGuidStronglyTypedId;

public sealed class MaintenancePlan : Entity<MaintenancePlanId>, IAggregateRoot
{
    public const int MaxCatchUpOccurrencesPerRun = 31;

    private MaintenancePlan()
    {
    }

    private MaintenancePlan(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string planCode,
        string? interval,
        DateOnly startsOn,
        string owner,
        DateTimeOffset? windowStartUtc,
        DateTimeOffset? windowEndUtc,
        decimal? runtimeHourInterval)
    {
        if ((windowStartUtc is null) != (windowEndUtc is null))
        {
            throw new ArgumentException("Maintenance availability window start and end must be provided together.");
        }

        windowStartUtc = windowStartUtc?.ToUniversalTime();
        windowEndUtc = windowEndUtc?.ToUniversalTime();
        if (windowStartUtc is not null && windowEndUtc is not null && windowEndUtc <= windowStartUtc)
        {
            throw new ArgumentException("Maintenance availability window end must be after start.");
        }

        // A plan must be triggered by a calendar interval, a runtime-hour interval, or both.
        // Runtime-only plans have no calendar interval and therefore never generate a calendar
        // occurrence — they only open work orders once accumulated runtime crosses the threshold.
        var normalizedInterval = string.IsNullOrWhiteSpace(interval) ? null : interval;
        if (normalizedInterval is null && runtimeHourInterval is null)
        {
            throw new ArgumentException("Maintenance plan must have a calendar interval, a runtime-hour interval, or both.");
        }

        if (normalizedInterval is not null)
        {
            _ = ParseIsoDayInterval(normalizedInterval);
        }

        if (runtimeHourInterval is not null && runtimeHourInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeHourInterval), "Maintenance runtime-hour interval must be positive.");
        }

        Id = new MaintenancePlanId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        DeviceAssetId = MaintenanceText.Required(deviceAssetId, nameof(deviceAssetId));
        PlanCode = MaintenanceText.Required(planCode, nameof(planCode));
        Interval = normalizedInterval;
        StartsOn = startsOn;
        Owner = MaintenanceText.Required(owner, nameof(owner));
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc;
        RuntimeHourInterval = runtimeHourInterval;
        LastGeneratedRuntimeHours = 0m;
        NextDueRuntimeHours = runtimeHourInterval;
        NextDueOn = normalizedInterval is null ? null : startsOn;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new MaintenancePlanCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string PlanCode { get; private set; } = string.Empty;

    /// <summary>ISO-8601 day interval (e.g. P30D) for the calendar trigger, or null for a runtime-only plan.</summary>
    public string? Interval { get; private set; }

    public DateOnly StartsOn { get; private set; }
    public DateOnly? LastGeneratedOn { get; private set; }

    /// <summary>Next calendar due date, or null for a runtime-only plan (no calendar trigger).</summary>
    public DateOnly? NextDueOn { get; private set; }
    public string Owner { get; private set; } = string.Empty;
    public DateTimeOffset? WindowStartUtc { get; private set; }
    public DateTimeOffset? WindowEndUtc { get; private set; }
    public decimal? RuntimeHourInterval { get; private set; }
    public decimal LastGeneratedRuntimeHours { get; private set; }
    public decimal? NextDueRuntimeHours { get; private set; }
    public bool Paused { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static MaintenancePlan Create(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        string planCode,
        string? interval,
        DateOnly startsOn,
        string owner,
        DateTimeOffset? windowStartUtc = null,
        DateTimeOffset? windowEndUtc = null,
        decimal? runtimeHourInterval = null)
    {
        return new MaintenancePlan(organizationId, environmentId, deviceAssetId, planCode, interval, startsOn, owner, windowStartUtc, windowEndUtc, runtimeHourInterval);
    }

    public bool IsDueOn(DateOnly businessDate)
    {
        return !Paused && NextDueOn is not null && NextDueOn <= businessDate;
    }

    public void Pause()
    {
        Paused = true;
    }

    public void MarkGenerated(DateOnly generatedOn)
    {
        _ = ConsumeDueDates(generatedOn);
    }

    public IReadOnlyCollection<DateOnly> ConsumeDueDates(DateOnly businessDate)
    {
        // Runtime-only plans (no calendar interval) never produce calendar occurrences.
        if (Paused || Interval is null || NextDueOn is null)
        {
            return [];
        }

        var dueDates = new List<DateOnly>();
        var intervalDays = ParseIsoDayInterval(Interval);
        var nextDueOn = NextDueOn.Value;
        while (nextDueOn <= businessDate && dueDates.Count < MaxCatchUpOccurrencesPerRun)
        {
            dueDates.Add(nextDueOn);
            LastGeneratedOn = nextDueOn;
            nextDueOn = nextDueOn.AddDays(intervalDays);
        }

        NextDueOn = nextDueOn;
        return dueDates;
    }

    public IReadOnlyCollection<decimal> ConsumeRuntimeDue(decimal runtimeHours)
    {
        if (Paused || RuntimeHourInterval is null || NextDueRuntimeHours is null || runtimeHours < NextDueRuntimeHours)
        {
            return [];
        }

        var thresholds = new List<decimal>();
        while (NextDueRuntimeHours is not null
            && runtimeHours >= NextDueRuntimeHours
            && thresholds.Count < MaxCatchUpOccurrencesPerRun)
        {
            thresholds.Add(NextDueRuntimeHours.Value);
            NextDueRuntimeHours += RuntimeHourInterval.Value;
        }

        LastGeneratedRuntimeHours = runtimeHours;
        return thresholds;
    }

    private static int ParseIsoDayInterval(string interval)
    {
        var normalized = MaintenanceText.Required(interval, nameof(interval)).ToUpperInvariant();
        if (!normalized.StartsWith('P') || !normalized.EndsWith('D') || normalized.Length <= 2)
        {
            throw new ArgumentException("Maintenance plan interval must be an ISO-8601 day interval such as P7D.", nameof(interval));
        }

        var digits = normalized[1..^1];
        if (!int.TryParse(digits, out var days) || days <= 0)
        {
            throw new ArgumentException("Maintenance plan interval days must be positive.", nameof(interval));
        }

        return days;
    }
}
