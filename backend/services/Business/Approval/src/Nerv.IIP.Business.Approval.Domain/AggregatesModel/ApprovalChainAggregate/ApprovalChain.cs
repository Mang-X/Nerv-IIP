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
        foreach (var templateStep in template.Steps.OrderBy(x => x.StepNo).ThenBy(x => x.ApproverType).ThenBy(x => x.ApproverRef))
        {
            steps.Add(ApprovalStep.FromTemplate(templateStep, StartedAtUtc));
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

    public ApprovalDecision ResolveStep(int stepNo, string actorType, string actorRef, string decision, string? comment)
    {
        var normalizedDecision = ApprovalText.Supported(decision, SupportedDecisions, nameof(decision));
        var normalizedActorType = ApprovalText.RequiredLower(actorType);
        var normalizedActorRef = ApprovalText.Required(actorRef);
        var normalizedComment = ApprovalText.Optional(comment);
        var sameActorDecision = decisions.SingleOrDefault(x =>
            x.StepNo == stepNo
            && x.ActorType == normalizedActorType
            && x.ActorRef == normalizedActorRef);
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

        if (steps.Where(x => x.StepNo < stepNo).Any(x => x.Status != ApprovalStepStatuses.Approved))
        {
            throw new InvalidOperationException("Approval steps must be resolved in sequence.");
        }

        var step = stepGroup.SingleOrDefault(x => x.MatchesApprover(normalizedActorType, normalizedActorRef) && x.Status == ApprovalStepStatuses.Pending)
            ?? throw new InvalidOperationException("No pending approval step is assigned to the actor.");
        var decidedAtUtc = DateTimeOffset.UtcNow;
        step.Resolve(normalizedActorType, normalizedActorRef, normalizedDecision, normalizedComment, decidedAtUtc);
        var approvalDecision = ApprovalDecision.Record(step, normalizedActorType, normalizedActorRef, normalizedDecision, normalizedComment, decidedAtUtc);
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

        if (steps.All(x => x.Status == ApprovalStepStatuses.Approved))
        {
            Status = ApprovalChainStatuses.Approved;
            CompletedAtUtc = decidedAtUtc;
            this.AddDomainEvent(new ApprovalApprovedDomainEvent(this, approvalDecision));
        }

        return approvalDecision;
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
        ApproverType = templateStep.ApproverType;
        ApproverRef = templateStep.ApproverRef;
        Status = ApprovalStepStatuses.Pending;
        DueAtUtc = templateStep.DueInHours.HasValue ? startedAtUtc.AddHours(templateStep.DueInHours.Value) : null;
    }

    public ApprovalChainId ChainId { get; private set; } = null!;
    public int StepNo { get; private set; }
    public string StepName { get; private set; } = string.Empty;
    // MVP metadata for UI/group reporting; approval progression still requires every step in the same StepNo to be approved.
    public string? ParallelGroupKey { get; private set; }
    public string ApproverType { get; private set; } = string.Empty;
    public string ApproverRef { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset? DueAtUtc { get; private set; }
    public string? ResolvedByActorType { get; private set; }
    public string? ResolvedByActorRef { get; private set; }
    public string? ResolvedDecision { get; private set; }
    public string? ResolvedComment { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

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
}

public sealed class ApprovalDecision : Entity<ApprovalDecisionId>
{
    private ApprovalDecision()
    {
    }

    private ApprovalDecision(ApprovalStep step, string actorType, string actorRef, string decision, string? comment, DateTimeOffset decidedAtUtc)
    {
        Id = new ApprovalDecisionId(Guid.CreateVersion7());
        StepId = step.Id;
        StepNo = step.StepNo;
        ActorType = actorType;
        ActorRef = actorRef;
        Decision = decision;
        Comment = comment;
        DecidedAtUtc = decidedAtUtc;
    }

    public ApprovalChainId ChainId { get; private set; } = null!;
    public ApprovalStepId StepId { get; private set; } = null!;
    public int StepNo { get; private set; }
    public string ActorType { get; private set; } = string.Empty;
    public string ActorRef { get; private set; } = string.Empty;
    public string Decision { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset DecidedAtUtc { get; private set; }

    internal static ApprovalDecision Record(ApprovalStep step, string actorType, string actorRef, string decision, string? comment, DateTimeOffset decidedAtUtc)
    {
        return new ApprovalDecision(step, actorType, actorRef, decision, comment, decidedAtUtc);
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
}

public static class ApprovalDecisions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Return = "return";
}
