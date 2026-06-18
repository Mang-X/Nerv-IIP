using Nerv.IIP.Business.Approval.Domain.AggregatesModel;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Domain.DomainEvents;

namespace Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

public partial record ApprovalChainId : IGuidStronglyTypedId;

public partial record ApprovalStepId : IGuidStronglyTypedId;

public partial record ApprovalDecisionId : IGuidStronglyTypedId;

public sealed class ApprovalChain : Entity<ApprovalChainId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedDecisions = ["approve", "reject", "return"];
    private readonly List<ApprovalStep> steps = [];
    private readonly List<ApprovalDecision> decisions = [];

    private ApprovalChain()
    {
    }

    private ApprovalChain(ApprovalTemplate template, ApprovalDocumentReference documentReference, string startedBy)
    {
        template.EnsureActive();
        Id = new ApprovalChainId(Guid.CreateVersion7());
        OrganizationId = template.OrganizationId;
        EnvironmentId = template.EnvironmentId;
        TemplateId = template.Id;
        TemplateCode = template.TemplateCode;
        TemplateVersion = template.Version;
        DocumentReference = documentReference;
        Status = ApprovalChainStatuses.Pending;
        StartedBy = ApprovalText.Required(startedBy);
        StartedAtUtc = DateTimeOffset.UtcNow;
        foreach (var templateStep in template.Steps
            .Where(x => ApprovalConditionMatcher.Matches(x.ConditionExpression, documentReference))
            .OrderBy(x => x.StepNo)
            .ThenBy(x => x.ApproverType)
            .ThenBy(x => x.ApproverRef))
        {
            steps.Add(ApprovalStep.FromTemplate(templateStep, StartedAtUtc));
        }

        if (steps.Count == 0)
        {
            throw new InvalidOperationException("Approval chain must contain at least one active step after condition routing.");
        }

        this.AddDomainEvent(new ApprovalStartedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public ApprovalTemplateId TemplateId { get; private set; } = null!;
    public string TemplateCode { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public ApprovalDocumentReference DocumentReference { get; private set; } = ApprovalDocumentReference.Empty;
    public string Status { get; private set; } = string.Empty;
    public string StartedBy { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<ApprovalStep> Steps => steps;
    public IReadOnlyCollection<ApprovalDecision> Decisions => decisions;

    public static ApprovalChain Start(ApprovalTemplate template, ApprovalDocumentReference documentReference, string startedBy)
    {
        return new ApprovalChain(template, documentReference, startedBy);
    }

    public ApprovalDecision ResolveStep(
        int stepNo,
        string actorType,
        string actorRef,
        string decision,
        string? comment,
        string? onBehalfOfActorType = null,
        string? onBehalfOfActorRef = null)
    {
        var normalizedDecision = ApprovalText.Supported(decision, SupportedDecisions, nameof(decision));
        var normalizedActorType = ApprovalText.RequiredLower(actorType);
        var normalizedActorRef = ApprovalText.Required(actorRef);
        var normalizedComment = ApprovalText.Optional(comment);
        var normalizedOnBehalfOfActorType = ApprovalText.Optional(onBehalfOfActorType)?.ToLowerInvariant();
        var normalizedOnBehalfOfActorRef = ApprovalText.Optional(onBehalfOfActorRef);
        var sameActorDecision = decisions.SingleOrDefault(x =>
            x.StepNo == stepNo
            && x.ActorType == normalizedActorType
            && x.ActorRef == normalizedActorRef
            && x.OnBehalfOfActorType == normalizedOnBehalfOfActorType
            && x.OnBehalfOfActorRef == normalizedOnBehalfOfActorRef);
        if (sameActorDecision is not null)
        {
            if (sameActorDecision.Decision == normalizedDecision && sameActorDecision.Comment == normalizedComment)
            {
                return sameActorDecision;
            }

            throw new InvalidOperationException("Approval decision conflicts with an existing decision from the same actor.");
        }

        if (Status is not ApprovalChainStatuses.Pending)
        {
            throw new InvalidOperationException("Approval chain is terminal.");
        }

        var stepGroup = steps.Where(x => x.StepNo == stepNo).ToArray();
        if (stepGroup.Length == 0)
        {
            throw new InvalidOperationException("Approval step was not found.");
        }

        if (CanActOnStepNo(stepNo) is false)
        {
            throw new InvalidOperationException("Approval steps must be resolved in sequence.");
        }

        var step = stepGroup.SingleOrDefault(x =>
                x.MatchesApprover(
                    normalizedOnBehalfOfActorType ?? normalizedActorType,
                    normalizedOnBehalfOfActorRef ?? normalizedActorRef)
                && x.Status == ApprovalStepStatuses.Pending)
            ?? throw new InvalidOperationException("No pending approval step is assigned to the actor.");
        var decidedAtUtc = DateTimeOffset.UtcNow;
        step.Resolve(normalizedActorType, normalizedActorRef, normalizedDecision, normalizedComment, decidedAtUtc);
        var approvalDecision = ApprovalDecision.Record(
            step,
            normalizedActorType,
            normalizedActorRef,
            normalizedDecision,
            normalizedComment,
            decidedAtUtc,
            normalizedOnBehalfOfActorType,
            normalizedOnBehalfOfActorRef);
        decisions.Add(approvalDecision);
        this.AddDomainEvent(new ApprovalStepResolvedDomainEvent(this, step, approvalDecision));

        if (normalizedDecision == ApprovalDecisions.Reject)
        {
            Status = ApprovalChainStatuses.Rejected;
            CompletedAtUtc = decidedAtUtc;
            this.AddDomainEvent(new ApprovalRejectedDomainEvent(this, approvalDecision));
            return approvalDecision;
        }

        if (normalizedDecision == ApprovalDecisions.Return)
        {
            Status = ApprovalChainStatuses.Returned;
            CompletedAtUtc = decidedAtUtc;
            this.AddDomainEvent(new ApprovalReturnedDomainEvent(this, approvalDecision));
            return approvalDecision;
        }

        if (normalizedDecision == ApprovalDecisions.Approve && step.CompletionPolicy == ApprovalCompletionPolicies.Any)
        {
            foreach (var skipped in stepGroup.Where(x => x.Id != step.Id && x.Status == ApprovalStepStatuses.Pending))
            {
                skipped.SkipBecauseAnyApproved(decidedAtUtc);
            }
        }

        if (steps.GroupBy(x => x.StepNo).All(ApprovalStep.IsGroupComplete))
        {
            Status = ApprovalChainStatuses.Approved;
            CompletedAtUtc = decidedAtUtc;
            this.AddDomainEvent(new ApprovalApprovedDomainEvent(this, approvalDecision));
        }

        return approvalDecision;
    }

    public int MarkOverdueSteps(DateTimeOffset nowUtc)
    {
        if (Status is not ApprovalChainStatuses.Pending)
        {
            return 0;
        }

        var marked = 0;
        foreach (var step in steps.Where(x => x.Status == ApprovalStepStatuses.Pending
                     && CanActOnStepNo(x.StepNo)
                     && x.OverdueNotifiedAtUtc is null
                     && x.DueAtUtc.HasValue
                     && x.DueAtUtc.Value <= nowUtc))
        {
            step.MarkOverdue(nowUtc);
            marked++;
            this.AddDomainEvent(new ApprovalStepOverdueDomainEvent(this, step, nowUtc));
        }

        return marked;
    }

    private bool CanActOnStepNo(int stepNo)
    {
        return steps
            .Where(x => x.StepNo < stepNo)
            .GroupBy(x => x.StepNo)
            .All(ApprovalStep.IsGroupComplete);
    }
}

public sealed class ApprovalStep : Entity<ApprovalStepId>
{
    private ApprovalStep()
    {
    }

    private ApprovalStep(ApprovalTemplateStep templateStep, DateTimeOffset startedAtUtc)
    {
        Id = new ApprovalStepId(Guid.CreateVersion7());
        StepNo = templateStep.StepNo;
        StepName = templateStep.StepName;
        ParallelGroupKey = templateStep.ParallelGroupKey;
        CompletionPolicy = templateStep.CompletionPolicy;
        ConditionExpression = templateStep.ConditionExpression;
        ApproverType = templateStep.ApproverType;
        ApproverRef = templateStep.ApproverRef;
        Status = ApprovalStepStatuses.Pending;
        DueAtUtc = templateStep.DueInHours.HasValue ? startedAtUtc.AddHours(templateStep.DueInHours.Value) : null;
    }

    public ApprovalChainId ChainId { get; private set; } = null!;
    public int StepNo { get; private set; }
    public string StepName { get; private set; } = string.Empty;
    public string? ParallelGroupKey { get; private set; }
    public string CompletionPolicy { get; private set; } = string.Empty;
    public string? ConditionExpression { get; private set; }
    public string ApproverType { get; private set; } = string.Empty;
    public string ApproverRef { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset? DueAtUtc { get; private set; }
    public string? ResolvedByActorType { get; private set; }
    public string? ResolvedByActorRef { get; private set; }
    public string? ResolvedDecision { get; private set; }
    public string? ResolvedComment { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public DateTimeOffset? OverdueNotifiedAtUtc { get; private set; }

    internal static ApprovalStep FromTemplate(ApprovalTemplateStep templateStep, DateTimeOffset startedAtUtc)
    {
        return new ApprovalStep(templateStep, startedAtUtc);
    }

    public bool MatchesApprover(string actorType, string actorRef)
    {
        return ApproverType == actorType && ApproverRef == actorRef;
    }

    public void Resolve(string actorType, string actorRef, string decision, string? comment, DateTimeOffset decidedAtUtc)
    {
        if (Status != ApprovalStepStatuses.Pending)
        {
            throw new InvalidOperationException("Approval step is already resolved.");
        }

        ResolvedByActorType = actorType;
        ResolvedByActorRef = actorRef;
        ResolvedDecision = decision;
        ResolvedComment = comment;
        ResolvedAtUtc = decidedAtUtc;
        Status = decision switch
        {
            ApprovalDecisions.Approve => ApprovalStepStatuses.Approved,
            ApprovalDecisions.Reject => ApprovalStepStatuses.Rejected,
            ApprovalDecisions.Return => ApprovalStepStatuses.Returned,
            _ => throw new ArgumentException("Unsupported approval decision.", nameof(decision)),
        };
    }

    public void SkipBecauseAnyApproved(DateTimeOffset decidedAtUtc)
    {
        if (Status != ApprovalStepStatuses.Pending)
        {
            return;
        }

        Status = ApprovalStepStatuses.Skipped;
        ResolvedDecision = ApprovalDecisions.Approve;
        ResolvedComment = "Skipped because another approver satisfied the any policy.";
        ResolvedAtUtc = decidedAtUtc;
    }

    public void MarkOverdue(DateTimeOffset nowUtc)
    {
        if (Status != ApprovalStepStatuses.Pending)
        {
            throw new InvalidOperationException("Only pending approval steps can be marked overdue.");
        }

        OverdueNotifiedAtUtc ??= nowUtc;
    }

    public static bool IsGroupComplete(IEnumerable<ApprovalStep> group)
    {
        var stepsInGroup = group.ToArray();
        if (stepsInGroup.Length == 0)
        {
            return true;
        }

        return stepsInGroup[0].CompletionPolicy == ApprovalCompletionPolicies.Any
            ? stepsInGroup.Any(x => x.Status == ApprovalStepStatuses.Approved)
            : stepsInGroup.All(x => x.Status is ApprovalStepStatuses.Approved or ApprovalStepStatuses.Skipped);
    }
}

public sealed class ApprovalDecision : Entity<ApprovalDecisionId>
{
    private ApprovalDecision()
    {
    }

    private ApprovalDecision(
        ApprovalStep step,
        string actorType,
        string actorRef,
        string decision,
        string? comment,
        DateTimeOffset decidedAtUtc,
        string? onBehalfOfActorType,
        string? onBehalfOfActorRef)
    {
        Id = new ApprovalDecisionId(Guid.CreateVersion7());
        StepId = step.Id;
        StepNo = step.StepNo;
        ActorType = actorType;
        ActorRef = actorRef;
        Decision = decision;
        Comment = comment;
        DecidedAtUtc = decidedAtUtc;
        OnBehalfOfActorType = onBehalfOfActorType;
        OnBehalfOfActorRef = onBehalfOfActorRef;
    }

    public ApprovalChainId ChainId { get; private set; } = null!;
    public ApprovalStepId StepId { get; private set; } = null!;
    public int StepNo { get; private set; }
    public string ActorType { get; private set; } = string.Empty;
    public string ActorRef { get; private set; } = string.Empty;
    public string Decision { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset DecidedAtUtc { get; private set; }
    public string? OnBehalfOfActorType { get; private set; }
    public string? OnBehalfOfActorRef { get; private set; }

    internal static ApprovalDecision Record(
        ApprovalStep step,
        string actorType,
        string actorRef,
        string decision,
        string? comment,
        DateTimeOffset decidedAtUtc,
        string? onBehalfOfActorType,
        string? onBehalfOfActorRef)
    {
        return new ApprovalDecision(step, actorType, actorRef, decision, comment, decidedAtUtc, onBehalfOfActorType, onBehalfOfActorRef);
    }
}

public sealed class ApprovalDocumentReference
{
    public static readonly ApprovalDocumentReference Empty = new();

    private ApprovalDocumentReference()
    {
    }

    public ApprovalDocumentReference(string sourceService, string documentType, string documentId, string? documentLineId)
    {
        SourceService = ApprovalText.RequiredLower(sourceService);
        DocumentType = ApprovalText.Required(documentType);
        DocumentId = ApprovalText.Required(documentId);
        DocumentLineId = ApprovalText.Optional(documentLineId);
    }

    public string SourceService { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public string DocumentId { get; private set; } = string.Empty;
    public string? DocumentLineId { get; private set; }
}

public static class ApprovalChainStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
}

public static class ApprovalStepStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Skipped = "skipped";
}

public static class ApprovalDecisions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Return = "return";
}

public static class ApprovalConditionMatcher
{
    public static bool IsValid(string? conditionExpression)
    {
        try
        {
            _ = Parse(conditionExpression);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool Matches(string? conditionExpression, ApprovalDocumentReference documentReference)
    {
        var parsedCondition = Parse(conditionExpression);
        if (parsedCondition is null)
        {
            return true;
        }

        var (key, value) = parsedCondition.Value;
        return key switch
        {
            "documenttype" => string.Equals(documentReference.DocumentType, value, StringComparison.OrdinalIgnoreCase),
            "sourceservice" => string.Equals(documentReference.SourceService, value, StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException($"Unsupported approval step condition key '{key}'."),
        };
    }

    private static (string Key, string Value)? Parse(string? conditionExpression)
    {
        if (string.IsNullOrWhiteSpace(conditionExpression))
        {
            return null;
        }

        var parts = conditionExpression.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new InvalidOperationException("Approval step condition must use key=value syntax.");
        }

        var key = parts[0].ToLowerInvariant();
        return key switch
        {
            "documenttype" or "sourceservice" => (key, parts[1]),
            _ => throw new InvalidOperationException($"Unsupported approval step condition key '{parts[0]}'."),
        };
    }
}
