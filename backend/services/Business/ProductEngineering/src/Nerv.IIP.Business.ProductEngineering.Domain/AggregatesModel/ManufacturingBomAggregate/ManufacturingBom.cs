using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;

public partial record ManufacturingBomId : IGuidStronglyTypedId;

public sealed class ManufacturingBom : Entity<ManufacturingBomId>, IAggregateRoot
{
    private readonly List<ManufacturingBomMaterialLine> materialLines = [];
    private readonly List<ManufacturingBomRecipeLine> recipeLines = [];

    private ManufacturingBom()
    {
    }

    private ManufacturingBom(string organizationId, string environmentId, string bomCode, string revision, string skuCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        BomCode = Required(bomCode);
        Revision = Required(revision);
        SkuCode = Required(skuCode);
        Status = EngineeringVersionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string BomCode { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string EngineeringBomVersionId { get; private set; } = string.Empty;
    public EngineeringVersionStatus Status { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ManufacturingBomMaterialLine> MaterialLines => materialLines.AsReadOnly();
    public IReadOnlyCollection<ManufacturingBomRecipeLine> RecipeLines => recipeLines.AsReadOnly();

    public static ManufacturingBom CreateDraft(string organizationId, string environmentId, string bomCode, string revision, string skuCode)
    {
        return new ManufacturingBom(organizationId, environmentId, bomCode, revision, skuCode);
    }

    public ManufacturingBom AddMaterialLine(string skuCode, decimal quantity, string unitOfMeasureCode, decimal scrapRate)
    {
        EnsureDraft();
        skuCode = Required(skuCode);
        if (materialLines.Any(x => x.SkuCode == skuCode))
        {
            throw new InvalidOperationException($"Manufacturing BOM already contains SKU '{skuCode}'.");
        }

        if (scrapRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scrapRate), "Scrap rate cannot be negative.");
        }

        materialLines.Add(new ManufacturingBomMaterialLine(skuCode, Positive(quantity, nameof(quantity)), Required(unitOfMeasureCode), scrapRate));
        Touch();
        return this;
    }

    public ManufacturingBom AddRecipeLine(string parameterCode, string targetValue, string unitOfMeasureCode)
    {
        EnsureDraft();
        parameterCode = Required(parameterCode);
        if (recipeLines.Any(x => x.ParameterCode == parameterCode))
        {
            throw new InvalidOperationException($"Recipe already contains parameter '{parameterCode}'.");
        }

        recipeLines.Add(new ManufacturingBomRecipeLine(parameterCode, Required(targetValue), Required(unitOfMeasureCode)));
        Touch();
        return this;
    }

    public void ReleaseFromEngineeringBom(string engineeringBomVersionId, EngineeringVersionStatus engineeringBomStatus, DateOnly effectiveDate)
    {
        EnsureDraft();
        if (engineeringBomStatus != EngineeringVersionStatus.Published)
        {
            throw new InvalidOperationException("Engineering BOM must be published before MBOM release.");
        }

        if (materialLines.Count == 0)
        {
            throw new InvalidOperationException("Manufacturing BOM must contain at least one material line before release.");
        }

        EngineeringBomVersionId = Required(engineeringBomVersionId);
        Status = EngineeringVersionStatus.Published;
        EffectiveDate = effectiveDate;
        Touch();
        AddDomainEvent(new ManufacturingBomReleasedDomainEvent(this));
    }

    private void EnsureDraft()
    {
        if (Status != EngineeringVersionStatus.Draft)
        {
            throw new InvalidOperationException("Released manufacturing BOM cannot be changed directly.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName, "Quantity must be positive.");
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}

public sealed class ManufacturingBomMaterialLine
{
    private ManufacturingBomMaterialLine()
    {
    }

    internal ManufacturingBomMaterialLine(string skuCode, decimal quantity, string unitOfMeasureCode, decimal scrapRate)
    {
        SkuCode = skuCode;
        Quantity = quantity;
        UnitOfMeasureCode = unitOfMeasureCode;
        ScrapRate = scrapRate;
    }

    public string SkuCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UnitOfMeasureCode { get; private set; } = string.Empty;
    public decimal ScrapRate { get; private set; }
}

public sealed class ManufacturingBomRecipeLine
{
    private ManufacturingBomRecipeLine()
    {
    }

    internal ManufacturingBomRecipeLine(string parameterCode, string targetValue, string unitOfMeasureCode)
    {
        ParameterCode = parameterCode;
        TargetValue = targetValue;
        UnitOfMeasureCode = unitOfMeasureCode;
    }

    public string ParameterCode { get; private set; } = string.Empty;
    public string TargetValue { get; private set; } = string.Empty;
    public string UnitOfMeasureCode { get; private set; } = string.Empty;
}
