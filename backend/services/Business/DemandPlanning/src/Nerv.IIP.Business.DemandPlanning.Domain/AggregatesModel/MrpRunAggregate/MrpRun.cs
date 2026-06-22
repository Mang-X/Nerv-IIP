using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;

public partial record MrpRunId : IGuidStronglyTypedId;

public enum MrpRunStatus
{
    Created = 0,
    Running = 1,
    Completed = 2,
}

public sealed record PlanningInputSnapshot(
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource,
    int DemandCount,
    int AvailabilityCount);

public static class PlanningInputDegradation
{
    public static IReadOnlyCollection<string> FromSnapshotSources(params string[] snapshotSources)
    {
        return snapshotSources
            .SelectMany(source => source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(ParseDegradedSource)
            .Where(source => source is not null)
            .Select(source => source!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ParseDegradedSource(string segment)
    {
        var separatorIndex = segment.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
        {
            return null;
        }

        var status = segment[(separatorIndex + 1)..];
        return string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
            ? segment[..separatorIndex]
            : null;
    }
}

public sealed class MrpRun : Entity<MrpRunId>, IAggregateRoot
{
    private MrpRun()
    {
    }

    private MrpRun(string organizationId, string environmentId, DateOnly horizonStart, DateOnly horizonEnd)
    {
        if (horizonEnd < horizonStart)
        {
            throw new ArgumentException("MRP horizon end must be on or after horizon start.", nameof(horizonEnd));
        }

        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        HorizonStart = horizonStart;
        HorizonEnd = horizonEnd;
        Status = MrpRunStatus.Created;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public DateOnly HorizonStart { get; private set; }
    public DateOnly HorizonEnd { get; private set; }
    public MrpRunStatus Status { get; private set; }
    public string ProductionEngineeringSnapshotSource { get; private set; } = string.Empty;
    public string InventorySnapshotSource { get; private set; } = string.Empty;
    public bool HasInputDegradation => InputDegradationSources.Count > 0;
    public IReadOnlyCollection<string> InputDegradationSources =>
        PlanningInputDegradation.FromSnapshotSources(ProductionEngineeringSnapshotSource, InventorySnapshotSource);
    public int DemandCount { get; private set; }
    public int AvailabilityCount { get; private set; }
    public int SuggestionCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static MrpRun Create(string organizationId, string environmentId, DateOnly horizonStart, DateOnly horizonEnd)
    {
        return new MrpRun(organizationId, environmentId, horizonStart, horizonEnd);
    }

    public void Start(PlanningInputSnapshot snapshot)
    {
        if (Status != MrpRunStatus.Created)
        {
            throw new InvalidOperationException("Only created MRP runs can be started.");
        }

        ProductionEngineeringSnapshotSource = DemandPlanningText.Required(snapshot.ProductionEngineeringSnapshotSource);
        InventorySnapshotSource = DemandPlanningText.Required(snapshot.InventorySnapshotSource);
        DemandCount = snapshot.DemandCount;
        AvailabilityCount = snapshot.AvailabilityCount;
        StartedAtUtc = DateTimeOffset.UtcNow;
        Status = MrpRunStatus.Running;
    }

    public void Complete(int suggestionCount)
    {
        if (Status != MrpRunStatus.Running)
        {
            throw new InvalidOperationException("Only running MRP runs can be completed.");
        }

        SuggestionCount = suggestionCount;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Status = MrpRunStatus.Completed;
        this.AddDomainEvent(new MrpRunCompletedDomainEvent(this));
    }
}
