using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using static Nerv.IIP.Business.ProductEngineering.Domain.ProductEngineeringGuards;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

public partial record ProductionVersionId : IGuidStronglyTypedId;

public enum EngineeringVersionStatus
{
    Draft,
    Published,
    Archived,
    Scheduled,
    Cancelled
}

public static class ProductionVersionStatus
{
    public const string Active = "active";
    public const string Archived = "archived";
}

public sealed class ProductionVersion : Entity<ProductionVersionId>, IAggregateRoot
{
    private ProductionVersion()
    {
    }

    private ProductionVersion(
        string organizationId,
        string environmentId,
        string skuCode,
        string mbomVersionId,
        string routingVersionId,
        DateOnly validFrom,
        DateOnly? validTo,
        decimal? lotSizeMin,
        decimal? lotSizeMax,
        int priority,
        bool isDefault)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        SkuCode = Required(skuCode);
        MbomVersionId = Required(mbomVersionId);
        RoutingVersionId = Required(routingVersionId);
        ValidFrom = validFrom;
        ValidTo = validTo;
        LotSizeMin = lotSizeMin;
        LotSizeMax = lotSizeMax;
        Priority = priority;
        IsDefault = isDefault;
        Status = ProductionVersionStatus.Active;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string MbomVersionId { get; private set; } = string.Empty;
    public string RoutingVersionId { get; private set; } = string.Empty;
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public decimal? LotSizeMin { get; private set; }
    public decimal? LotSizeMax { get; private set; }
    public int Priority { get; private set; }
    public bool IsDefault { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static ProductionVersion Create(
        string organizationId,
        string environmentId,
        string skuCode,
        string mbomVersionId,
        string routingVersionId,
        DateOnly validFrom,
        DateOnly? validTo,
        decimal? lotSizeMin,
        decimal? lotSizeMax,
        int priority,
        bool isDefault,
        EngineeringVersionStatus mbomStatus,
        EngineeringVersionStatus routingStatus)
    {
        EnsurePublished(mbomStatus, "MBOM");
        EnsurePublished(routingStatus, "Routing");
        EnsureEffectiveWindow(validFrom, validTo);
        EnsureLotSizeWindow(lotSizeMin, lotSizeMax);

        var version = new ProductionVersion(
            organizationId,
            environmentId,
            skuCode,
            mbomVersionId,
            routingVersionId,
            validFrom,
            validTo,
            lotSizeMin,
            lotSizeMax,
            priority,
            isDefault);
        version.AddDomainEvent(new ProductionVersionCreatedDomainEvent(version));
        return version;
    }

    public void UpdateBinding(
        string mbomVersionId,
        string routingVersionId,
        DateOnly validFrom,
        DateOnly? validTo,
        decimal? lotSizeMin,
        decimal? lotSizeMax,
        int priority,
        bool isDefault,
        EngineeringVersionStatus mbomStatus,
        EngineeringVersionStatus routingStatus)
    {
        EnsureNotArchived();
        EnsurePublished(mbomStatus, "MBOM");
        EnsurePublished(routingStatus, "Routing");
        EnsureEffectiveWindow(validFrom, validTo);
        EnsureLotSizeWindow(lotSizeMin, lotSizeMax);

        MbomVersionId = Required(mbomVersionId);
        RoutingVersionId = Required(routingVersionId);
        ValidFrom = validFrom;
        ValidTo = validTo;
        LotSizeMin = lotSizeMin;
        LotSizeMax = lotSizeMax;
        Priority = priority;
        IsDefault = isDefault;
        Touch();
        AddDomainEvent(new ProductionVersionUpdatedDomainEvent(this));
    }

    public void Archive(string reason)
    {
        _ = Required(reason);
        EnsureNotArchived();
        Status = ProductionVersionStatus.Archived;
        Touch();
        AddDomainEvent(new ProductionVersionArchivedDomainEvent(this));
    }

    public void SupersedeWith(ProductionVersion successor, DateOnly effectiveDate, string reason)
    {
        ArgumentNullException.ThrowIfNull(successor);
        EnsureNotArchived();
        if (successor.Status != ProductionVersionStatus.Active || !string.Equals(successor.SkuCode, SkuCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Successor production version must be active for the same SKU.");
        }

        if (effectiveDate <= ValidFrom)
        {
            throw new ArgumentException("Supersede effective date must be after the superseded production version ValidFrom.", nameof(effectiveDate));
        }

        if (successor.ValidTo is not null && effectiveDate > successor.ValidTo.Value)
        {
            throw new ArgumentException("Successor production version effective window must include the supersede effective date.", nameof(effectiveDate));
        }

        ValidTo = effectiveDate.AddDays(-1);
        successor.ValidFrom = effectiveDate;
        successor.Touch();
        successor.AddDomainEvent(new ProductionVersionUpdatedDomainEvent(successor));
        Archive(reason);
    }

    public bool IsResolvableFor(DateOnly effectiveDate, decimal lotSize)
    {
        return Status == ProductionVersionStatus.Active &&
               ValidFrom <= effectiveDate &&
               (ValidTo is null || effectiveDate <= ValidTo.Value) &&
               (LotSizeMin is null || LotSizeMin.Value <= lotSize) &&
               (LotSizeMax is null || lotSize <= LotSizeMax.Value);
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void EnsureNotArchived()
    {
        if (Status == ProductionVersionStatus.Archived)
        {
            throw new InvalidOperationException("Archived production version cannot be changed or referenced by new work orders.");
        }
    }

    private static void EnsurePublished(EngineeringVersionStatus status, string versionKind)
    {
        if (status != EngineeringVersionStatus.Published)
        {
            throw new InvalidOperationException($"{versionKind} version must be published before it can be bound to a production version.");
        }
    }

    private static void EnsureEffectiveWindow(DateOnly validFrom, DateOnly? validTo)
    {
        if (validTo is not null && validFrom > validTo.Value)
        {
            throw new ArgumentException("ValidFrom must be earlier than or equal to ValidTo.", nameof(validTo));
        }
    }

    private static void EnsureLotSizeWindow(decimal? lotSizeMin, decimal? lotSizeMax)
    {
        if (lotSizeMin is not null && lotSizeMin.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lotSizeMin), "Lot size minimum cannot be negative.");
        }

        if (lotSizeMax is not null && lotSizeMax.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lotSizeMax), "Lot size maximum cannot be negative.");
        }

        if (lotSizeMin is not null && lotSizeMax is not null && lotSizeMin.Value > lotSizeMax.Value)
        {
            throw new ArgumentException("LotSizeMin must be less than or equal to LotSizeMax.", nameof(lotSizeMax));
        }
    }
}
