using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;

namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;

public partial record CorrectiveActionId : IGuidStronglyTypedId;

public partial record CorrectiveActionItemId : IGuidStronglyTypedId;

public sealed class CorrectiveAction : Entity<CorrectiveActionId>, IAggregateRoot
{
    private CorrectiveAction()
    {
    }

    private CorrectiveAction(
        string organizationId,
        string environmentId,
        string capaCode,
        string? sourceNcrId,
        string rootCause,
        string containmentAction,
        string ownerUserId,
        DateTimeOffset dueAtUtc)
    {
        Id = new CorrectiveActionId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        CapaCode = Required(capaCode);
        SourceNcrId = Optional(sourceNcrId);
        RootCause = Required(rootCause);
        ContainmentAction = Required(containmentAction);
        OwnerUserId = Required(ownerUserId);
        DueAtUtc = dueAtUtc == default ? throw new ArgumentException("CAPA due time is required.", nameof(dueAtUtc)) : dueAtUtc;
        Status = "open";
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new CorrectiveActionOpenedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CapaCode { get; private set; } = string.Empty;
    public string? SourceNcrId { get; private set; }
    public string RootCause { get; private set; } = string.Empty;
    public string ContainmentAction { get; private set; } = string.Empty;
    public string OwnerUserId { get; private set; } = string.Empty;
    public DateTimeOffset DueAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? EffectivenessVerifiedByUserId { get; private set; }
    public string? EffectivenessResult { get; private set; }
    public InspectionRecordId? EffectivenessInspectionRecordId { get; private set; }
    public DateTimeOffset? EffectivenessVerifiedAtUtc { get; private set; }
    public string? CloseApprovalChainId { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public List<CorrectiveActionItem> Actions { get; private set; } = [];

    public static CorrectiveAction OpenFromNcr(
        string organizationId,
        string environmentId,
        string capaCode,
        NonconformanceReport ncr,
        string rootCause,
        string containmentAction,
        string ownerUserId,
        DateTimeOffset dueAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ncr);
        if (ncr.OrganizationId != organizationId || ncr.EnvironmentId != environmentId)
        {
            throw new InvalidOperationException("CAPA scope must match source NCR scope.");
        }

        return new CorrectiveAction(
            organizationId,
            environmentId,
            capaCode,
            ncr.Id.ToString(),
            rootCause,
            containmentAction,
            ownerUserId,
            dueAtUtc);
    }

    public static CorrectiveAction OpenStandalone(
        string organizationId,
        string environmentId,
        string capaCode,
        string rootCause,
        string containmentAction,
        string ownerUserId,
        DateTimeOffset dueAtUtc)
    {
        return new CorrectiveAction(
            organizationId,
            environmentId,
            capaCode,
            null,
            rootCause,
            containmentAction,
            ownerUserId,
            dueAtUtc);
    }

    public void AddAction(string actionType, string description, string ownerUserId, DateTimeOffset dueAtUtc)
    {
        EnsureOpen();
        Actions.Add(CorrectiveActionItem.Create(actionType, description, ownerUserId, dueAtUtc));
        Touch();
    }

    public void CompleteAction(CorrectiveActionItemId actionItemId, string completedByUserId, DateTimeOffset completedAtUtc)
    {
        EnsureOpen();
        var action = Actions.SingleOrDefault(x => x.Id == actionItemId)
            ?? throw new InvalidOperationException($"CAPA action '{actionItemId}' was not found.");
        action.Complete(completedByUserId, completedAtUtc);
        Touch();
    }

    public void VerifyEffectiveness(
        string verifiedByUserId,
        string result,
        DateTimeOffset verifiedAtUtc,
        InspectionRecordId? effectivenessInspectionRecordId = null,
        string? effectivenessInspectionResult = null)
    {
        EnsureOpen();
        if (!Actions.Any(x => x.ActionType is "corrective" or "preventive"))
        {
            throw new InvalidOperationException("CAPA requires corrective or preventive action before effectiveness verification.");
        }

        if (Actions.Any(x => x.Status != "completed"))
        {
            throw new InvalidOperationException("CAPA effectiveness cannot be verified until all action items are completed.");
        }

        if (effectivenessInspectionRecordId is null)
        {
            throw new InvalidOperationException("CAPA effectiveness verification requires a passed verification inspection.");
        }

        if (!string.Equals(effectivenessInspectionResult, "passed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CAPA effectiveness verification inspection must be passed.");
        }

        EffectivenessVerifiedByUserId = Required(verifiedByUserId);
        EffectivenessResult = Required(result);
        EffectivenessInspectionRecordId = effectivenessInspectionRecordId;
        EffectivenessVerifiedAtUtc = verifiedAtUtc == default
            ? throw new ArgumentException("Effectiveness verification time is required.", nameof(verifiedAtUtc))
            : verifiedAtUtc;
        Status = "effectiveness-verified";
        Touch();
        this.AddDomainEvent(new CorrectiveActionEffectivenessVerifiedDomainEvent(this));
    }

    public void Close(string closedByUserId, string? closeApprovalChainId = null)
    {
        if (Status != "effectiveness-verified")
        {
            throw new InvalidOperationException("CAPA cannot close before effectiveness is verified.");
        }

        if (EffectivenessInspectionRecordId is null)
        {
            throw new InvalidOperationException("CAPA cannot close before a passed verification inspection is linked.");
        }

        CloseApprovalChainId = Optional(closeApprovalChainId);
        ClosedByUserId = Required(closedByUserId);
        ClosedAtUtc = DateTimeOffset.UtcNow;
        Status = "closed";
        Touch();
        this.AddDomainEvent(new CorrectiveActionClosedDomainEvent(this));
    }

    private void EnsureOpen()
    {
        if (Status == "closed")
        {
            throw new InvalidOperationException("Closed CAPA cannot be changed.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed class CorrectiveActionItem : Entity<CorrectiveActionItemId>
{
    private static readonly HashSet<string> SupportedActionTypes =
    [
        "containment",
        "corrective",
        "preventive",
    ];

    private CorrectiveActionItem()
    {
    }

    private CorrectiveActionItem(string actionType, string description, string ownerUserId, DateTimeOffset dueAtUtc)
    {
        Id = new CorrectiveActionItemId(Guid.CreateVersion7());
        ActionType = Supported(actionType);
        Description = Required(description);
        OwnerUserId = Required(ownerUserId);
        DueAtUtc = dueAtUtc == default ? throw new ArgumentException("Action due time is required.", nameof(dueAtUtc)) : dueAtUtc;
        Status = "open";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public CorrectiveActionId CorrectiveActionId { get; private set; } = null!;
    public string ActionType { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string OwnerUserId { get; private set; } = string.Empty;
    public DateTimeOffset DueAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? CompletedByUserId { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static CorrectiveActionItem Create(string actionType, string description, string ownerUserId, DateTimeOffset dueAtUtc)
    {
        return new CorrectiveActionItem(actionType, description, ownerUserId, dueAtUtc);
    }

    internal void Complete(string completedByUserId, DateTimeOffset completedAtUtc)
    {
        if (Status == "completed")
        {
            return;
        }

        CompletedByUserId = Required(completedByUserId);
        if (completedAtUtc == default)
        {
            throw new ArgumentException("Action completion time is required.", nameof(completedAtUtc));
        }

        CompletedAtUtc = completedAtUtc;
        Status = "completed";
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string Supported(string value)
    {
        var normalized = Required(value).ToLowerInvariant();
        return SupportedActionTypes.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported CAPA action type '{value}'.", nameof(value));
    }
}
