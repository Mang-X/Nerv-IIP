using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;

public partial record WorkCenterId : IGuidStronglyTypedId;

public class WorkCenter : Entity<WorkCenterId>, IAggregateRoot
{
    protected WorkCenter()
    {
    }

    private WorkCenter(
        string organizationId,
        string environmentId,
        string code,
        string name,
        int capacityMinutesPerDay,
        string resourceType,
        string plantCode,
        string lineCode,
        string? workshopCode,
        string defaultCalendarCode,
        string capacityUnit,
        bool finiteCapacity,
        decimal utilizationRate,
        decimal efficiencyRate,
        int numberOfCapacities,
        string? costCenterCode,
        bool bottleneck)
    {
        if (capacityMinutesPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityMinutesPerDay), "Capacity minutes per day must be positive.");
        }

        ValidateCapacityFactors(utilizationRate, efficiencyRate, numberOfCapacities);
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        Code = Required(code);
        Name = Required(name);
        CapacityMinutesPerDay = capacityMinutesPerDay;
        ResourceType = Required(resourceType);
        PlantCode = Optional(plantCode);
        LineCode = Optional(lineCode);
        WorkshopCode = NormalizeOptional(workshopCode);
        DefaultCalendarCode = Optional(defaultCalendarCode);
        CapacityUnit = Required(capacityUnit);
        FiniteCapacity = finiteCapacity;
        UtilizationRate = utilizationRate;
        EfficiencyRate = efficiencyRate;
        NumberOfCapacities = numberOfCapacities;
        CostCenterCode = NormalizeOptional(costCenterCode);
        Bottleneck = bottleneck;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int CapacityMinutesPerDay { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public string PlantCode { get; private set; } = string.Empty;
    public string LineCode { get; private set; } = string.Empty;
    public string? WorkshopCode { get; private set; }
    public string DefaultCalendarCode { get; private set; } = string.Empty;
    public string CapacityUnit { get; private set; } = string.Empty;
    public bool FiniteCapacity { get; private set; }
    public decimal UtilizationRate { get; private set; } = 1m;
    public decimal EfficiencyRate { get; private set; } = 1m;
    public int NumberOfCapacities { get; private set; } = 1;
    public string? CostCenterCode { get; private set; }
    public bool Bottleneck { get; private set; }
    public decimal EffectiveCapacityMinutesPerDay => CapacityMinutesPerDay * UtilizationRate * EfficiencyRate * NumberOfCapacities;
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static WorkCenter Create(string organizationId, string environmentId, string code, string name, int capacityMinutesPerDay)
    {
        return new WorkCenter(organizationId, environmentId, code, name, capacityMinutesPerDay, "work-center", string.Empty, string.Empty, null, string.Empty, "minute", true, 1m, 1m, 1, null, false);
    }

    public static WorkCenter CreateResource(
        string organizationId,
        string environmentId,
        string code,
        string name,
        int capacityMinutesPerDay,
        string resourceType,
        string plantCode,
        string lineCode,
        string defaultCalendarCode,
        string capacityUnit,
        bool finiteCapacity)
    {
        return CreateResource(organizationId, environmentId, code, name, capacityMinutesPerDay, resourceType, plantCode, lineCode, null, defaultCalendarCode, capacityUnit, finiteCapacity);
    }

    public static WorkCenter CreateResource(
        string organizationId,
        string environmentId,
        string code,
        string name,
        int capacityMinutesPerDay,
        string resourceType,
        string plantCode,
        string lineCode,
        string? workshopCode,
        string defaultCalendarCode,
        string capacityUnit,
        bool finiteCapacity,
        decimal utilizationRate = 1m,
        decimal efficiencyRate = 1m,
        int numberOfCapacities = 1,
        string? costCenterCode = null,
        bool bottleneck = false)
    {
        return new WorkCenter(organizationId, environmentId, code, name, capacityMinutesPerDay, resourceType, plantCode, lineCode, workshopCode, defaultCalendarCode, capacityUnit, finiteCapacity, utilizationRate, efficiencyRate, numberOfCapacities, costCenterCode, bottleneck);
    }

    public void UpdateResource(
        string name,
        int capacityMinutesPerDay,
        string resourceType,
        string plantCode,
        string lineCode,
        string defaultCalendarCode,
        string capacityUnit,
        bool finiteCapacity)
    {
        UpdateResource(name, capacityMinutesPerDay, resourceType, plantCode, lineCode, null, defaultCalendarCode, capacityUnit, finiteCapacity);
    }

    public void UpdateResource(
        string name,
        int capacityMinutesPerDay,
        string resourceType,
        string plantCode,
        string lineCode,
        string? workshopCode,
        string defaultCalendarCode,
        string capacityUnit,
        bool finiteCapacity,
        decimal? utilizationRate = null,
        decimal? efficiencyRate = null,
        int? numberOfCapacities = null,
        string? costCenterCode = null,
        bool? bottleneck = null)
    {
        if (capacityMinutesPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityMinutesPerDay), "Capacity minutes per day must be positive.");
        }

        var nextUtilizationRate = utilizationRate ?? UtilizationRate;
        var nextEfficiencyRate = efficiencyRate ?? EfficiencyRate;
        var nextNumberOfCapacities = numberOfCapacities ?? NumberOfCapacities;
        ValidateCapacityFactors(nextUtilizationRate, nextEfficiencyRate, nextNumberOfCapacities);
        EnsureEnabled();
        Name = Required(name);
        CapacityMinutesPerDay = capacityMinutesPerDay;
        ResourceType = Required(resourceType);
        PlantCode = Optional(plantCode);
        LineCode = Optional(lineCode);
        WorkshopCode = NormalizeOptional(workshopCode);
        DefaultCalendarCode = Optional(defaultCalendarCode);
        CapacityUnit = Required(capacityUnit);
        FiniteCapacity = finiteCapacity;
        UtilizationRate = nextUtilizationRate;
        EfficiencyRate = nextEfficiencyRate;
        NumberOfCapacities = nextNumberOfCapacities;
        CostCenterCode = NormalizeOptional(costCenterCode) ?? CostCenterCode;
        Bottleneck = bottleneck ?? Bottleneck;
        Touch();
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code, validReason));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code));
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
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code));
        this.AddDomainEvent(new ResourceChangedDomainEvent(nameof(WorkCenter), OrganizationId, EnvironmentId, Code));
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

    private static string Optional(string value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateCapacityFactors(decimal utilizationRate, decimal efficiencyRate, int numberOfCapacities)
    {
        if (utilizationRate <= 0 || utilizationRate > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(utilizationRate), "Utilization rate must be in the range (0, 1].");
        }

        if (efficiencyRate <= 0 || efficiencyRate > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(efficiencyRate), "Efficiency rate must be in the range (0, 1].");
        }

        if (numberOfCapacities <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numberOfCapacities), "Number of capacities must be positive.");
        }
    }
}
