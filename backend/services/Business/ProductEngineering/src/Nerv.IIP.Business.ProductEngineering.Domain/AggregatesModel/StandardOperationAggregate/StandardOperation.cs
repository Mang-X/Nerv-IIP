using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using static Nerv.IIP.Business.ProductEngineering.Domain.ProductEngineeringGuards;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;

public partial record StandardOperationId : IGuidStronglyTypedId;

public sealed class StandardOperation : Entity<StandardOperationId>, IAggregateRoot
{
    private StandardOperation()
    {
    }

    private StandardOperation(
        string organizationId,
        string environmentId,
        string operationCode,
        string operationName,
        string defaultWorkCenterCode,
        int standardSetupMinutes,
        int standardRunMinutes,
        string controlKey,
        bool requiresReporting,
        bool requiresQualityInspection,
        bool isOutsourced,
        string? description)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        OperationCode = Required(operationCode);
        CreatedAtUtc = DateTime.UtcNow;
        ApplyDetails(
            operationName,
            defaultWorkCenterCode,
            standardSetupMinutes,
            standardRunMinutes,
            controlKey,
            requiresReporting,
            requiresQualityInspection,
            isOutsourced,
            description);
        Enabled = true;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OperationCode { get; private set; } = string.Empty;
    public string OperationName { get; private set; } = string.Empty;
    public string DefaultWorkCenterCode { get; private set; } = string.Empty;
    public int StandardSetupMinutes { get; private set; }
    public int StandardRunMinutes { get; private set; }
    public int StandardMinutes => StandardSetupMinutes + StandardRunMinutes;
    public string ControlKey { get; private set; } = string.Empty;
    public bool RequiresReporting { get; private set; }
    public bool RequiresQualityInspection { get; private set; }
    public bool IsOutsourced { get; private set; }
    public string? Description { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static StandardOperation Create(
        string organizationId,
        string environmentId,
        string operationCode,
        string operationName,
        string defaultWorkCenterCode,
        int standardSetupMinutes,
        int standardRunMinutes,
        string controlKey,
        bool requiresReporting,
        bool requiresQualityInspection,
        bool isOutsourced,
        string? description)
    {
        var operation = new StandardOperation(
            organizationId,
            environmentId,
            operationCode,
            operationName,
            defaultWorkCenterCode,
            standardSetupMinutes,
            standardRunMinutes,
            controlKey,
            requiresReporting,
            requiresQualityInspection,
            isOutsourced,
            description);
        operation.AddDomainEvent(new StandardOperationCreatedDomainEvent(operation));
        return operation;
    }

    public void Update(
        string operationName,
        string defaultWorkCenterCode,
        int standardSetupMinutes,
        int standardRunMinutes,
        string controlKey,
        bool requiresReporting,
        bool requiresQualityInspection,
        bool isOutsourced,
        string? description)
    {
        EnsureNotArchived();
        ApplyDetails(
            operationName,
            defaultWorkCenterCode,
            standardSetupMinutes,
            standardRunMinutes,
            controlKey,
            requiresReporting,
            requiresQualityInspection,
            isOutsourced,
            description);
        Touch();
        AddDomainEvent(new StandardOperationUpdatedDomainEvent(this));
    }

    public void Archive(string reason)
    {
        _ = Required(reason);
        EnsureNotArchived();
        Enabled = false;
        Touch();
        AddDomainEvent(new StandardOperationArchivedDomainEvent(this));
    }

    private void ApplyDetails(
        string operationName,
        string defaultWorkCenterCode,
        int standardSetupMinutes,
        int standardRunMinutes,
        string controlKey,
        bool requiresReporting,
        bool requiresQualityInspection,
        bool isOutsourced,
        string? description)
    {
        if (standardSetupMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardSetupMinutes), "Standard setup minutes cannot be negative.");
        }

        if (standardRunMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardRunMinutes), "Standard run minutes must be positive.");
        }

        OperationName = Required(operationName);
        DefaultWorkCenterCode = Required(defaultWorkCenterCode);
        StandardSetupMinutes = standardSetupMinutes;
        StandardRunMinutes = standardRunMinutes;
        ControlKey = Required(controlKey);
        RequiresReporting = requiresReporting;
        RequiresQualityInspection = requiresQualityInspection;
        IsOutsourced = isOutsourced;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void EnsureNotArchived()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Archived standard operation cannot be changed or selected by new routing versions.");
        }
    }
}
