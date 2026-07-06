using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;

public partial record MesEngineeringChangeWorkOrderImpactId : IGuidStronglyTypedId;

public static class MesEngineeringChangeImpactStatuses
{
    public const string ArchivedProductionVersion = "archived-production-version";
    public const string PendingDecision = "pending-decision";
    public const string AutoRebound = "auto-rebound";
    public const string BlockedForManualConfirmation = "blocked-for-manual-confirmation";
    public const string Decided = "decided";
}

public static class MesEngineeringChangeDecisions
{
    public const string ContinueWithArchivedVersion = "continue-with-archived-version";
    public const string AbortWorkOrder = "abort-work-order";
}

public sealed class MesEngineeringChangeWorkOrderImpact : Entity<MesEngineeringChangeWorkOrderImpactId>, IAggregateRoot
{
    private MesEngineeringChangeWorkOrderImpact()
    {
    }

    private MesEngineeringChangeWorkOrderImpact(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string workOrderStatusAtDetection,
        string changeNumber,
        string archivedProductionVersionId,
        string? supersededByProductionVersionId,
        DateOnly effectiveDate,
        string status,
        DateTimeOffset detectedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        SkuId = DomainGuard.Required(skuId, nameof(skuId));
        WorkOrderStatusAtDetection = DomainGuard.Required(workOrderStatusAtDetection, nameof(workOrderStatusAtDetection));
        ChangeNumber = DomainGuard.Required(changeNumber, nameof(changeNumber));
        ArchivedProductionVersionId = DomainGuard.Required(archivedProductionVersionId, nameof(archivedProductionVersionId));
        SupersededByProductionVersionId = string.IsNullOrWhiteSpace(supersededByProductionVersionId) ? null : supersededByProductionVersionId.Trim();
        EffectiveDate = effectiveDate;
        Status = DomainGuard.Required(status, nameof(status));
        DetectedAtUtc = detectedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SkuId { get; private set; } = string.Empty;
    public string WorkOrderStatusAtDetection { get; private set; } = string.Empty;
    public string ChangeNumber { get; private set; } = string.Empty;
    public string ArchivedProductionVersionId { get; private set; } = string.Empty;
    public string? SupersededByProductionVersionId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset DetectedAtUtc { get; private set; }
    public string? Decision { get; private set; }
    public string? DecidedBy { get; private set; }
    public string? DecisionReason { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public static MesEngineeringChangeWorkOrderImpact ArchivedProductionVersion(
        string organizationId,
        string environmentId,
        string changeNumber,
        string archivedProductionVersionId,
        string? supersededByProductionVersionId,
        DateOnly effectiveDate,
        DateTimeOffset detectedAtUtc)
    {
        return new MesEngineeringChangeWorkOrderImpact(
            organizationId,
            environmentId,
            $"production-version:{DomainGuard.Required(archivedProductionVersionId, nameof(archivedProductionVersionId))}",
            "*",
            "*",
            changeNumber,
            archivedProductionVersionId,
            supersededByProductionVersionId,
            effectiveDate,
            MesEngineeringChangeImpactStatuses.ArchivedProductionVersion,
            detectedAtUtc);
    }

    public static MesEngineeringChangeWorkOrderImpact PendingDecision(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string workOrderStatusAtDetection,
        string changeNumber,
        string archivedProductionVersionId,
        string? supersededByProductionVersionId,
        DateOnly effectiveDate,
        DateTimeOffset detectedAtUtc)
    {
        return CreateWorkOrderImpact(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            workOrderStatusAtDetection,
            changeNumber,
            archivedProductionVersionId,
            supersededByProductionVersionId,
            effectiveDate,
            MesEngineeringChangeImpactStatuses.PendingDecision,
            detectedAtUtc);
    }

    public static MesEngineeringChangeWorkOrderImpact AutoRebound(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string workOrderStatusAtDetection,
        string changeNumber,
        string archivedProductionVersionId,
        string supersededByProductionVersionId,
        DateOnly effectiveDate,
        DateTimeOffset detectedAtUtc)
    {
        return CreateWorkOrderImpact(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            workOrderStatusAtDetection,
            changeNumber,
            archivedProductionVersionId,
            supersededByProductionVersionId,
            effectiveDate,
            MesEngineeringChangeImpactStatuses.AutoRebound,
            detectedAtUtc);
    }

    public static MesEngineeringChangeWorkOrderImpact BlockedForManualConfirmation(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string workOrderStatusAtDetection,
        string changeNumber,
        string archivedProductionVersionId,
        string? supersededByProductionVersionId,
        DateOnly effectiveDate,
        DateTimeOffset detectedAtUtc)
    {
        return CreateWorkOrderImpact(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            workOrderStatusAtDetection,
            changeNumber,
            archivedProductionVersionId,
            supersededByProductionVersionId,
            effectiveDate,
            MesEngineeringChangeImpactStatuses.BlockedForManualConfirmation,
            detectedAtUtc);
    }

    public void RecordDecision(string decision, string decidedBy, string reason, DateTimeOffset decidedAtUtc)
    {
        decision = DomainGuard.Required(decision, nameof(decision));
        if (decision is not MesEngineeringChangeDecisions.ContinueWithArchivedVersion and not MesEngineeringChangeDecisions.AbortWorkOrder)
        {
            throw new InvalidOperationException($"Unsupported MES engineering change decision '{decision}'.");
        }

        if (Status == MesEngineeringChangeImpactStatuses.ArchivedProductionVersion)
        {
            throw new InvalidOperationException("Archived production version marker cannot receive a work order decision.");
        }

        Decision = decision;
        DecidedBy = DomainGuard.Required(decidedBy, nameof(decidedBy));
        DecisionReason = DomainGuard.Required(reason, nameof(reason));
        DecidedAtUtc = decidedAtUtc;
        Status = MesEngineeringChangeImpactStatuses.Decided;
    }

    private static MesEngineeringChangeWorkOrderImpact CreateWorkOrderImpact(
        string organizationId,
        string environmentId,
        string workOrderId,
        string skuId,
        string workOrderStatusAtDetection,
        string changeNumber,
        string archivedProductionVersionId,
        string? supersededByProductionVersionId,
        DateOnly effectiveDate,
        string status,
        DateTimeOffset detectedAtUtc)
    {
        var impact = new MesEngineeringChangeWorkOrderImpact(
            organizationId,
            environmentId,
            workOrderId,
            skuId,
            workOrderStatusAtDetection,
            changeNumber,
            archivedProductionVersionId,
            supersededByProductionVersionId,
            effectiveDate,
            status,
            detectedAtUtc);
        impact.AddDomainEvent(new MesEngineeringChangeWorkOrderImpactDetectedDomainEvent(impact));
        return impact;
    }
}
