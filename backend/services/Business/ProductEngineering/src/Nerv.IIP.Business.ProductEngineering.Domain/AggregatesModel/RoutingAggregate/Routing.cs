using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using static Nerv.IIP.Business.ProductEngineering.Domain.ProductEngineeringGuards;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;

public partial record RoutingId : IGuidStronglyTypedId;

public sealed class Routing : Entity<RoutingId>, IAggregateRoot
{
    private readonly List<RoutingOperation> operations = [];

    private Routing()
    {
    }

    private Routing(string organizationId, string environmentId, string routingCode, string revision, string skuCode)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        RoutingCode = Required(routingCode);
        Revision = Required(revision);
        SkuCode = Required(skuCode);
        Status = EngineeringVersionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RoutingCode { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public EngineeringVersionStatus Status { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<RoutingOperation> Operations => operations.OrderBy(x => x.Sequence).ToArray();

    public static Routing CreateDraft(string organizationId, string environmentId, string routingCode, string revision, string skuCode)
    {
        return new Routing(organizationId, environmentId, routingCode, revision, skuCode);
    }

    public Routing AddOperation(int sequence, string workCenterCode, string operationCode, string operationName, int standardMinutes)
    {
        EnsureDraft();
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Operation sequence must be positive.");
        }

        if (standardMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardMinutes), "Standard minutes must be positive.");
        }

        if (operations.Any(x => x.Sequence == sequence))
        {
            throw new InvalidOperationException($"Routing already contains operation sequence '{sequence}'.");
        }

        operations.Add(new RoutingOperation(sequence, Required(workCenterCode), Required(operationCode), Required(operationName), standardMinutes));
        Touch();
        return this;
    }

    public void Release(DateOnly effectiveDate)
    {
        EnsureDraft();
        if (operations.Count == 0)
        {
            throw new InvalidOperationException("Routing must contain at least one operation before release.");
        }

        Status = EngineeringVersionStatus.Published;
        EffectiveDate = effectiveDate;
        Touch();
        AddDomainEvent(new RoutingReleasedDomainEvent(this));
    }

    private void EnsureDraft()
    {
        if (Status != EngineeringVersionStatus.Draft)
        {
            throw new InvalidOperationException("Released routing cannot be changed directly.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class RoutingOperation
{
    private RoutingOperation()
    {
    }

    internal RoutingOperation(int sequence, string workCenterCode, string operationCode, string operationName, int standardMinutes)
    {
        Sequence = sequence;
        WorkCenterCode = workCenterCode;
        OperationCode = operationCode;
        OperationName = operationName;
        StandardMinutes = standardMinutes;
    }

    public int Sequence { get; private set; }
    public string WorkCenterCode { get; private set; } = string.Empty;
    public string OperationCode { get; private set; } = string.Empty;
    public string OperationName { get; private set; } = string.Empty;
    public int StandardMinutes { get; private set; }
}
