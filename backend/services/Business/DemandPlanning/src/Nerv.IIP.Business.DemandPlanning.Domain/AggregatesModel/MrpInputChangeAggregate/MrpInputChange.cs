namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpInputChangeAggregate;

public partial record MrpInputChangeId : IGuidStronglyTypedId;

public enum MrpInputChangeOperation
{
    Created,
    Updated,
    Deleted,
}

/// <summary>A durable observation of one source input before and after a change.</summary>
public sealed class MrpInputChange : Entity<MrpInputChangeId>, IAggregateRoot
{
    private MrpInputChange() { }

    private MrpInputChange(
        string organizationId, string environmentId, string inputType, string? demandType,
        string sourceReference, string sourceLineReference,
        DateTimeOffset occurredAtUtc, MrpInputChangeOperation operation,
        DateOnly? previousStartDate, DateOnly? previousEndDate, bool previouslyEligible,
        DateOnly? currentStartDate, DateOnly? currentEndDate, bool currentlyEligible)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        InputType = DemandPlanningText.Required(inputType, nameof(inputType));
        DemandType = inputType == "demand"
            ? DemandPlanningText.Required(demandType ?? string.Empty, nameof(demandType))
            : demandType;
        SourceReference = DemandPlanningText.Required(sourceReference, nameof(sourceReference));
        SourceLineReference = sourceLineReference ?? throw new ArgumentNullException(nameof(sourceLineReference));
        if (occurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Change time must be UTC.", nameof(occurredAtUtc));
        if (previousStartDate > previousEndDate || currentStartDate > currentEndDate)
            throw new ArgumentException("Input date interval must have an ordered start and end.");
        if (previouslyEligible && (previousStartDate is null || previousEndDate is null))
            throw new ArgumentException("An eligible previous input requires a date interval.");
        if (currentlyEligible && (currentStartDate is null || currentEndDate is null))
            throw new ArgumentException("An eligible current input requires a date interval.");
        if (operation == MrpInputChangeOperation.Deleted && currentlyEligible)
            throw new ArgumentException("A deleted input cannot remain eligible.", nameof(currentlyEligible));

        OccurredAtUtc = occurredAtUtc;
        Operation = operation;
        PreviousStartDate = previousStartDate;
        PreviousEndDate = previousEndDate;
        PreviouslyEligible = previouslyEligible;
        CurrentStartDate = currentStartDate;
        CurrentEndDate = currentEndDate;
        CurrentlyEligible = currentlyEligible;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string InputType { get; private set; } = string.Empty;
    public string? DemandType { get; private set; }
    public string SourceReference { get; private set; } = string.Empty;
    public string SourceLineReference { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public MrpInputChangeOperation Operation { get; private set; }
    public DateOnly? PreviousStartDate { get; private set; }
    public DateOnly? PreviousEndDate { get; private set; }
    public bool PreviouslyEligible { get; private set; }
    public DateOnly? CurrentStartDate { get; private set; }
    public DateOnly? CurrentEndDate { get; private set; }
    public bool CurrentlyEligible { get; private set; }

    public static MrpInputChange Record(
        string organizationId, string environmentId, string inputType, string? demandType,
        string sourceReference, string sourceLineReference,
        DateTimeOffset occurredAtUtc, MrpInputChangeOperation operation,
        DateOnly? previousStartDate, DateOnly? previousEndDate, bool previouslyEligible,
        DateOnly? currentStartDate, DateOnly? currentEndDate, bool currentlyEligible)
        => new(organizationId, environmentId, inputType, demandType, sourceReference, sourceLineReference,
            occurredAtUtc, operation, previousStartDate, previousEndDate, previouslyEligible,
            currentStartDate, currentEndDate, currentlyEligible);
}
