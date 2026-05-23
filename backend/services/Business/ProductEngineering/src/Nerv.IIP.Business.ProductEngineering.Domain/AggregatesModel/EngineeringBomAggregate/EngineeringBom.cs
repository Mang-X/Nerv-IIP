using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using static Nerv.IIP.Business.ProductEngineering.Domain.ProductEngineeringGuards;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;

public partial record EngineeringBomId : IGuidStronglyTypedId;

public sealed class EngineeringBom : Entity<EngineeringBomId>, IAggregateRoot
{
    private readonly List<EngineeringBomLine> lines = [];

    private EngineeringBom()
    {
    }

    private EngineeringBom(string organizationId, string environmentId, string bomCode, string revision, string parentItemCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        BomCode = Required(bomCode);
        Revision = Required(revision);
        ParentItemCode = Required(parentItemCode);
        Status = EngineeringVersionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string BomCode { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string ParentItemCode { get; private set; } = string.Empty;
    public EngineeringVersionStatus Status { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<EngineeringBomLine> Lines => lines.AsReadOnly();

    public static EngineeringBom CreateDraft(string organizationId, string environmentId, string bomCode, string revision, string parentItemCode)
    {
        return new EngineeringBom(organizationId, environmentId, bomCode, revision, parentItemCode);
    }

    public EngineeringBom AddLine(string childItemCode, decimal quantity, string unitOfMeasureCode)
    {
        EnsureDraft();
        childItemCode = Required(childItemCode);
        if (lines.Any(x => x.ChildItemCode == childItemCode))
        {
            throw new InvalidOperationException($"Engineering BOM already contains child item '{childItemCode}'.");
        }

        lines.Add(new EngineeringBomLine(childItemCode, Positive(quantity, nameof(quantity)), Required(unitOfMeasureCode)));
        Touch();
        return this;
    }

    public void Release(DateOnly effectiveDate)
    {
        EnsureDraft();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Engineering BOM must contain at least one component before release.");
        }

        Status = EngineeringVersionStatus.Published;
        EffectiveDate = effectiveDate;
        Touch();
        AddDomainEvent(new EngineeringBomReleasedDomainEvent(this));
    }

    private void EnsureDraft()
    {
        if (Status != EngineeringVersionStatus.Draft)
        {
            throw new InvalidOperationException("Released engineering BOM cannot be changed directly.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class EngineeringBomLine
{
    private EngineeringBomLine()
    {
    }

    internal EngineeringBomLine(string childItemCode, decimal quantity, string unitOfMeasureCode)
    {
        ChildItemCode = childItemCode;
        Quantity = quantity;
        UnitOfMeasureCode = unitOfMeasureCode;
    }

    public string ChildItemCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UnitOfMeasureCode { get; private set; } = string.Empty;
}
