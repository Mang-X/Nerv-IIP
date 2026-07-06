using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;

public partial record ForecastInputId : IGuidStronglyTypedId;

public sealed class ForecastInput : Entity<ForecastInputId>, IAggregateRoot
{
    private ForecastInput()
    {
    }

    private ForecastInput(
        string organizationId,
        string environmentId,
        string forecastReference,
        string skuCode,
        string uomCode,
        string siteCode,
        DateOnly periodStartDate,
        DateOnly periodEndDate,
        decimal quantity,
        int backwardConsumptionDays,
        int forwardConsumptionDays)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        ForecastReference = DemandPlanningText.Required(forecastReference, nameof(forecastReference));
        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        Apply(periodStartDate, periodEndDate, quantity, backwardConsumptionDays, forwardConsumptionDays);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new ForecastInputCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ForecastReference { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public DateOnly PeriodStartDate { get; private set; }
    public DateOnly PeriodEndDate { get; private set; }
    public decimal Quantity { get; private set; }
    public int BackwardConsumptionDays { get; private set; }
    public int ForwardConsumptionDays { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ForecastInput Create(
        string organizationId,
        string environmentId,
        string forecastReference,
        string skuCode,
        string uomCode,
        string siteCode,
        DateOnly periodStartDate,
        DateOnly periodEndDate,
        decimal quantity,
        int backwardConsumptionDays,
        int forwardConsumptionDays)
    {
        return new ForecastInput(
            organizationId,
            environmentId,
            forecastReference,
            skuCode,
            uomCode,
            siteCode,
            periodStartDate,
            periodEndDate,
            quantity,
            backwardConsumptionDays,
            forwardConsumptionDays);
    }

    public void Update(
        string skuCode,
        string uomCode,
        string siteCode,
        DateOnly periodStartDate,
        DateOnly periodEndDate,
        decimal quantity,
        int backwardConsumptionDays,
        int forwardConsumptionDays)
    {
        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        Apply(periodStartDate, periodEndDate, quantity, backwardConsumptionDays, forwardConsumptionDays);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private void Apply(
        DateOnly periodStartDate,
        DateOnly periodEndDate,
        decimal quantity,
        int backwardConsumptionDays,
        int forwardConsumptionDays)
    {
        if (periodEndDate < periodStartDate)
        {
            throw new ArgumentException("Forecast period end date must be on or after start date.", nameof(periodEndDate));
        }

        if (backwardConsumptionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(backwardConsumptionDays), "Consumption days cannot be negative.");
        }

        if (forwardConsumptionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(forwardConsumptionDays), "Consumption days cannot be negative.");
        }

        PeriodStartDate = periodStartDate;
        PeriodEndDate = periodEndDate;
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        BackwardConsumptionDays = backwardConsumptionDays;
        ForwardConsumptionDays = forwardConsumptionDays;
    }
}
