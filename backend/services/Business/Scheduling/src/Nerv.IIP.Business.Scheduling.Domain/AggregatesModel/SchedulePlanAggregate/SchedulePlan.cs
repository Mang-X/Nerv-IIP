using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

public partial record ScheduleProblemSnapshotId : IGuidStronglyTypedId;
public partial record SchedulePlanId : IGuidStronglyTypedId;
public partial record SchedulePlanAssignmentId : IGuidStronglyTypedId;
public partial record SchedulePlanResourceLoadId : IGuidStronglyTypedId;
public partial record SchedulePlanConflictId : IGuidStronglyTypedId;
public partial record SchedulePlanUnscheduledOperationId : IGuidStronglyTypedId;

public enum SchedulePlanLifecycleStatus
{
    Generated = 0,
    Released = 1,
}

public sealed class ScheduleProblemSnapshot : Entity<ScheduleProblemSnapshotId>
{
    private ScheduleProblemSnapshot()
    {
    }

    public ScheduleProblemSnapshot(
        string problemId,
        int contractVersion,
        string organizationId,
        string environmentId,
        string problemFingerprint,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc,
        DateTimeOffset capturedAtUtc)
    {
        ProblemId = Required(problemId, nameof(problemId));
        ContractVersion = contractVersion;
        OrganizationId = Required(organizationId, nameof(organizationId));
        EnvironmentId = Required(environmentId, nameof(environmentId));
        ProblemFingerprint = Required(problemFingerprint, nameof(problemFingerprint));
        HorizonStartUtc = horizonStartUtc;
        HorizonEndUtc = horizonEndUtc;
        CapturedAtUtc = capturedAtUtc;
    }

    public string ProblemId { get; private set; } = string.Empty;
    public int ContractVersion { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ProblemFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset HorizonStartUtc { get; private set; }
    public DateTimeOffset HorizonEndUtc { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }

    private static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName ?? nameof(value));
        }

        return value.Trim();
    }
}

public sealed class SchedulePlan : Entity<SchedulePlanId>, IAggregateRoot
{
    private readonly List<SchedulePlanAssignment> assignments = [];
    private readonly List<SchedulePlanResourceLoad> resourceLoads = [];
    private readonly List<SchedulePlanConflict> conflicts = [];
    private readonly List<SchedulePlanUnscheduledOperation> unscheduledOperations = [];

    private SchedulePlan()
    {
    }

    private SchedulePlan(
        string organizationId,
        string environmentId,
        SchedulePlanContract contract)
    {
        if (contract.Status == SchedulePlanStatusContract.Released)
        {
            throw new InvalidOperationException("Released contract plans cannot be persisted as newly generated plans.");
        }

        OrganizationId = Required(organizationId, nameof(organizationId));
        EnvironmentId = Required(environmentId, nameof(environmentId));
        PlanId = Required(contract.PlanId, nameof(contract.PlanId));
        ProblemId = Required(contract.ProblemId, nameof(contract.ProblemId));
        ProblemFingerprint = Required(contract.ProblemFingerprint, nameof(contract.ProblemFingerprint));
        AlgorithmVersion = Required(contract.AlgorithmVersion, nameof(contract.AlgorithmVersion));
        ContractVersion = contract.ContractVersion;
        GeneratedAtUtc = contract.GeneratedAtUtc;
        Status = SchedulePlanLifecycleStatus.Generated;

        foreach (var assignment in contract.Assignments)
        {
            AddAssignmentCore(assignment);
        }

        foreach (var load in contract.ResourceLoads)
        {
            resourceLoads.Add(SchedulePlanResourceLoad.FromContract(load));
        }

        foreach (var conflict in contract.Conflicts)
        {
            var entity = SchedulePlanConflict.FromContract(conflict);
            conflicts.Add(entity);
        }

        foreach (var unscheduled in contract.UnscheduledOperations)
        {
            unscheduledOperations.Add(SchedulePlanUnscheduledOperation.FromContract(unscheduled));
        }

        this.AddDomainEvent(new SchedulePlanGeneratedDomainEvent(this));
        foreach (var conflict in conflicts)
        {
            this.AddDomainEvent(new ScheduleConflictDetectedDomainEvent(this, conflict));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PlanId { get; private set; } = string.Empty;
    public string ProblemId { get; private set; } = string.Empty;
    public string ProblemFingerprint { get; private set; } = string.Empty;
    public string AlgorithmVersion { get; private set; } = string.Empty;
    public int ContractVersion { get; private set; }
    public SchedulePlanLifecycleStatus Status { get; private set; }
    public DateTimeOffset GeneratedAtUtc { get; private set; }
    public DateTimeOffset? ReleasedAtUtc { get; private set; }
    public IReadOnlyCollection<SchedulePlanAssignment> Assignments => assignments;
    public IReadOnlyCollection<SchedulePlanResourceLoad> ResourceLoads => resourceLoads;
    public IReadOnlyCollection<SchedulePlanConflict> Conflicts => conflicts;
    public IReadOnlyCollection<SchedulePlanUnscheduledOperation> UnscheduledOperations => unscheduledOperations;

    public static SchedulePlan FromGeneratedContract(
        string organizationId,
        string environmentId,
        SchedulePlanContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return new SchedulePlan(organizationId, environmentId, contract);
    }

    public void Release(DateTimeOffset releasedAtUtc)
    {
        if (Status == SchedulePlanLifecycleStatus.Released)
        {
            return;
        }

        Status = SchedulePlanLifecycleStatus.Released;
        ReleasedAtUtc = releasedAtUtc;
        this.AddDomainEvent(new SchedulePlanReleasedDomainEvent(this));
    }

    public void ReplaceGeneratedPlan(SchedulePlanContract contract)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(contract);

        if (contract.Status == SchedulePlanStatusContract.Released)
        {
            throw new InvalidOperationException("Released contract plans cannot replace generated plans.");
        }

        var replacementPlanId = Required(contract.PlanId, nameof(contract.PlanId));
        if (!string.Equals(replacementPlanId, PlanId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Replacement schedule plan contract must keep the same public plan identity.");
        }

        ProblemId = Required(contract.ProblemId, nameof(contract.ProblemId));
        ProblemFingerprint = Required(contract.ProblemFingerprint, nameof(contract.ProblemFingerprint));
        AlgorithmVersion = Required(contract.AlgorithmVersion, nameof(contract.AlgorithmVersion));
        ContractVersion = contract.ContractVersion;
        GeneratedAtUtc = contract.GeneratedAtUtc;
        assignments.Clear();
        resourceLoads.Clear();
        conflicts.Clear();
        unscheduledOperations.Clear();

        foreach (var assignment in contract.Assignments)
        {
            AddAssignmentCore(assignment);
        }

        foreach (var load in contract.ResourceLoads)
        {
            resourceLoads.Add(SchedulePlanResourceLoad.FromContract(load));
        }

        foreach (var conflict in contract.Conflicts)
        {
            var entity = SchedulePlanConflict.FromContract(conflict);
            conflicts.Add(entity);
            this.AddDomainEvent(new ScheduleConflictDetectedDomainEvent(this, entity));
        }

        foreach (var unscheduled in contract.UnscheduledOperations)
        {
            unscheduledOperations.Add(SchedulePlanUnscheduledOperation.FromContract(unscheduled));
        }

        this.AddDomainEvent(new SchedulePlanGeneratedDomainEvent(this));
    }

    public void AddAssignment(ScheduleAssignmentContract assignment)
    {
        EnsureMutable();
        AddAssignmentCore(assignment);
    }

    private void AddAssignmentCore(ScheduleAssignmentContract assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        assignments.Add(SchedulePlanAssignment.FromContract(assignment));
    }

    private void EnsureMutable()
    {
        if (Status == SchedulePlanLifecycleStatus.Released)
        {
            throw new InvalidOperationException("Released schedule plans are immutable.");
        }
    }

    private static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName ?? nameof(value));
        }

        return value.Trim();
    }
}

public sealed class SchedulePlanAssignment : Entity<SchedulePlanAssignmentId>
{
    private SchedulePlanAssignment()
    {
    }

    private SchedulePlanAssignment(ScheduleAssignmentContract contract)
    {
        AssignmentId = Required(contract.AssignmentId, nameof(contract.AssignmentId));
        WorkOrderId = Required(contract.OrderId, nameof(contract.OrderId));
        OperationId = Required(contract.OperationId, nameof(contract.OperationId));
        OperationSequence = contract.OperationSequence;
        ResourceId = Required(contract.ResourceId, nameof(contract.ResourceId));
        WorkCenterId = Required(contract.WorkCenterId, nameof(contract.WorkCenterId));
        StartUtc = contract.StartUtc;
        EndUtc = contract.EndUtc;
        IsLocked = contract.IsLocked;
        ExplanationCode = Required(contract.ExplanationCode, nameof(contract.ExplanationCode));
    }

    public SchedulePlanId SchedulePlanId { get; private set; } = null!;
    public string AssignmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public int OperationSequence { get; private set; }
    public string ResourceId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public DateTimeOffset StartUtc { get; private set; }
    public DateTimeOffset EndUtc { get; private set; }
    public bool IsLocked { get; private set; }
    public string ExplanationCode { get; private set; } = string.Empty;

    public static SchedulePlanAssignment FromContract(ScheduleAssignmentContract contract)
    {
        return new SchedulePlanAssignment(contract);
    }

    private static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName ?? nameof(value));
        }

        return value.Trim();
    }
}

public sealed class SchedulePlanResourceLoad : Entity<SchedulePlanResourceLoadId>
{
    private SchedulePlanResourceLoad()
    {
    }

    private SchedulePlanResourceLoad(ScheduleResourceLoadContract contract)
    {
        ResourceId = Required(contract.ResourceId, nameof(contract.ResourceId));
        WindowStartUtc = contract.WindowStartUtc;
        WindowEndUtc = contract.WindowEndUtc;
        AssignedMinutes = contract.AssignedMinutes;
        AvailableMinutes = contract.AvailableMinutes;
        Utilization = contract.Utilization;
    }

    public SchedulePlanId SchedulePlanId { get; private set; } = null!;
    public string ResourceId { get; private set; } = string.Empty;
    public DateTimeOffset WindowStartUtc { get; private set; }
    public DateTimeOffset WindowEndUtc { get; private set; }
    public int AssignedMinutes { get; private set; }
    public int AvailableMinutes { get; private set; }
    public decimal Utilization { get; private set; }

    public static SchedulePlanResourceLoad FromContract(ScheduleResourceLoadContract contract)
    {
        return new SchedulePlanResourceLoad(contract);
    }

    private static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName ?? nameof(value));
        }

        return value.Trim();
    }
}

public sealed class SchedulePlanConflict : Entity<SchedulePlanConflictId>
{
    private SchedulePlanConflict()
    {
    }

    private SchedulePlanConflict(ScheduleConflictContract contract)
    {
        ConflictPublicId = Required(contract.ConflictId, nameof(contract.ConflictId));
        ReasonCode = contract.ReasonCode;
        Severity = contract.Severity;
        WorkOrderId = contract.OrderId ?? string.Empty;
        OperationId = contract.OperationId ?? string.Empty;
        ResourceId = contract.ResourceId ?? string.Empty;
        Message = Required(contract.Message, nameof(contract.Message));
    }

    public SchedulePlanId SchedulePlanId { get; private set; } = null!;
    public string ConflictPublicId { get; private set; } = string.Empty;
    public ScheduleConflictReasonCodeContract ReasonCode { get; private set; }
    public ScheduleConflictSeverityContract Severity { get; private set; }
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    public static SchedulePlanConflict FromContract(ScheduleConflictContract contract)
    {
        return new SchedulePlanConflict(contract);
    }

    private static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName ?? nameof(value));
        }

        return value.Trim();
    }
}

public sealed class SchedulePlanUnscheduledOperation : Entity<SchedulePlanUnscheduledOperationId>
{
    private SchedulePlanUnscheduledOperation()
    {
    }

    private SchedulePlanUnscheduledOperation(UnscheduledOperationContract contract)
    {
        WorkOrderId = Required(contract.OrderId, nameof(contract.OrderId));
        OperationId = Required(contract.OperationId, nameof(contract.OperationId));
        ReasonCode = contract.ReasonCode;
        Message = Required(contract.Message, nameof(contract.Message));
    }

    public SchedulePlanId SchedulePlanId { get; private set; } = null!;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationId { get; private set; } = string.Empty;
    public ScheduleConflictReasonCodeContract ReasonCode { get; private set; }
    public string Message { get; private set; } = string.Empty;

    public static SchedulePlanUnscheduledOperation FromContract(UnscheduledOperationContract contract)
    {
        return new SchedulePlanUnscheduledOperation(contract);
    }

    private static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName ?? nameof(value));
        }

        return value.Trim();
    }
}
