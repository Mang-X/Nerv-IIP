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
        return AddOperation(
            sequence,
            workCenterCode,
            operationCode,
            operationName,
            setupMinutes: 0,
            runMinutes: standardMinutes,
            teardownMinutes: 0,
            controlKey: "standard",
            requiresReporting: true,
            requiresQualityInspection: false,
            isOutsourced: false);
    }

    public Routing AddOperation(
        int sequence,
        string workCenterCode,
        string operationCode,
        string operationName,
        int setupMinutes,
        int runMinutes,
        int teardownMinutes,
        string controlKey,
        bool requiresReporting,
        bool requiresQualityInspection,
        bool isOutsourced)
    {
        EnsureDraft();
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Operation sequence must be positive.");
        }

        if (setupMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(setupMinutes), "Setup minutes cannot be negative.");
        }

        if (runMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runMinutes), "Run minutes must be positive.");
        }

        if (teardownMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(teardownMinutes), "Teardown minutes cannot be negative.");
        }

        if (operations.Any(x => x.Sequence == sequence))
        {
            throw new InvalidOperationException($"Routing already contains operation sequence '{sequence}'.");
        }

        operations.Add(new RoutingOperation(
            sequence,
            Required(workCenterCode),
            Required(operationCode),
            Required(operationName),
            setupMinutes,
            runMinutes,
            teardownMinutes,
            Required(controlKey),
            requiresReporting,
            requiresQualityInspection,
            isOutsourced));
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

    public void Archive(string reason)
    {
        _ = Required(reason);
        if (Status == EngineeringVersionStatus.Archived)
        {
            return;
        }

        if (Status != EngineeringVersionStatus.Published)
        {
            throw new InvalidOperationException("Only released routing versions can be archived by an engineering change.");
        }

        Status = EngineeringVersionStatus.Archived;
        Touch();
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

    internal RoutingOperation(
        int sequence,
        string workCenterCode,
        string operationCode,
        string operationName,
        int setupMinutes,
        int runMinutes,
        int teardownMinutes,
        string controlKey,
        bool requiresReporting,
        bool requiresQualityInspection,
        bool isOutsourced)
    {
        Sequence = sequence;
        WorkCenterCode = workCenterCode;
        OperationCode = operationCode;
        OperationName = operationName;
        SetupMinutes = setupMinutes;
        RunMinutes = runMinutes;
        TeardownMinutes = teardownMinutes;
        StandardMinutes = setupMinutes + runMinutes + teardownMinutes;
        ControlKey = controlKey;
        RequiresReporting = requiresReporting;
        RequiresQualityInspection = requiresQualityInspection;
        IsOutsourced = isOutsourced;
    }

    public int Sequence { get; private set; }
    public string WorkCenterCode { get; private set; } = string.Empty;
    public string OperationCode { get; private set; } = string.Empty;
    public string OperationName { get; private set; } = string.Empty;
    public int SetupMinutes { get; private set; }
    public int RunMinutes { get; private set; }
    public int TeardownMinutes { get; private set; }
    public int StandardMinutes { get; private set; }
    public string ControlKey { get; private set; } = string.Empty;
    public bool RequiresReporting { get; private set; }
    public bool RequiresQualityInspection { get; private set; }
    public bool IsOutsourced { get; private set; }
}
