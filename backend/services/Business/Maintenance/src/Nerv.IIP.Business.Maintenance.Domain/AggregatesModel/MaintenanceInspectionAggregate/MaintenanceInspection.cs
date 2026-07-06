using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Domain.DomainEvents;

namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;

public partial record MaintenanceInspectionId : IGuidStronglyTypedId;

public partial record MaintenanceInspectionMeasurementId : IGuidStronglyTypedId;

public sealed record MaintenanceInspectionMeasurementDraft(
    string CharacteristicCode,
    decimal MeasuredValue,
    string UomCode,
    decimal? LowerSpecLimit = null,
    decimal? UpperSpecLimit = null);

public static class MaintenanceInspectionResults
{
    private static readonly string[] FailedResults = ["failed", "fail", "blocked", "not-ok", "not ok", "nok", "ng", "不合格"];

    public static string Normalize(string result)
    {
        return MaintenanceText.Required(result, nameof(result)).ToLowerInvariant();
    }

    public static bool IsFailed(string result)
    {
        return FailedResults.Contains(Normalize(result), StringComparer.Ordinal);
    }
}

public sealed class MaintenanceInspection : Entity<MaintenanceInspectionId>, IAggregateRoot
{
    private readonly List<MaintenanceInspectionMeasurement> measurements = [];

    private MaintenanceInspection()
    {
    }

    private MaintenanceInspection(
        string organizationId,
        string environmentId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc,
        MaintenancePlanId? planId,
        MaintenanceWorkOrderId? workOrderId,
        IEnumerable<MaintenanceInspectionMeasurementDraft>? measurementDrafts = null)
    {
        if (planId is null && workOrderId is null)
        {
            throw new ArgumentException("Inspection must reference a maintenance plan or work order.");
        }

        Id = new MaintenanceInspectionId(Guid.CreateVersion7());
        OrganizationId = MaintenanceText.Required(organizationId, nameof(organizationId));
        EnvironmentId = MaintenanceText.Required(environmentId, nameof(environmentId));
        Inspector = MaintenanceText.Required(inspector, nameof(inspector));
        Result = MaintenanceInspectionResults.Normalize(result);
        InspectedAtUtc = inspectedAtUtc.ToUniversalTime();
        PlanId = planId;
        WorkOrderId = workOrderId;
        foreach (var draft in measurementDrafts ?? [])
        {
            measurements.Add(MaintenanceInspectionMeasurement.Create(draft));
        }

        this.AddDomainEvent(new MaintenanceInspectionRecordedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public MaintenancePlanId? PlanId { get; private set; }
    public MaintenanceWorkOrderId? WorkOrderId { get; private set; }
    public string Inspector { get; private set; } = string.Empty;
    public string Result { get; private set; } = string.Empty;
    public DateTimeOffset InspectedAtUtc { get; private set; }
    public IReadOnlyCollection<MaintenanceInspectionMeasurement> Measurements => measurements;

    public static MaintenanceInspection Record(
        string organizationId,
        string environmentId,
        MaintenancePlanId? planId,
        MaintenanceWorkOrderId? workOrderId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc,
        IEnumerable<MaintenanceInspectionMeasurementDraft>? measurements = null)
    {
        return new MaintenanceInspection(organizationId, environmentId, inspector, result, inspectedAtUtc, planId, workOrderId, measurements);
    }

    public static MaintenanceInspection RecordForPlan(
        string organizationId,
        string environmentId,
        MaintenancePlanId planId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc,
        IEnumerable<MaintenanceInspectionMeasurementDraft>? measurements = null)
    {
        return new MaintenanceInspection(organizationId, environmentId, inspector, result, inspectedAtUtc, planId, null, measurements);
    }

    public static MaintenanceInspection RecordForWorkOrder(
        string organizationId,
        string environmentId,
        MaintenanceWorkOrderId workOrderId,
        string inspector,
        string result,
        DateTimeOffset inspectedAtUtc,
        IEnumerable<MaintenanceInspectionMeasurementDraft>? measurements = null)
    {
        return new MaintenanceInspection(organizationId, environmentId, inspector, result, inspectedAtUtc, null, workOrderId, measurements);
    }
}

public sealed class MaintenanceInspectionMeasurement : Entity<MaintenanceInspectionMeasurementId>
{
    private MaintenanceInspectionMeasurement()
    {
    }

    private MaintenanceInspectionMeasurement(MaintenanceInspectionMeasurementDraft draft)
    {
        if (draft.LowerSpecLimit is not null && draft.UpperSpecLimit is not null && draft.LowerSpecLimit > draft.UpperSpecLimit)
        {
            throw new ArgumentException("Lower spec limit cannot be greater than upper spec limit.", nameof(draft));
        }

        Id = new MaintenanceInspectionMeasurementId(Guid.CreateVersion7());
        CharacteristicCode = MaintenanceText.Required(draft.CharacteristicCode, nameof(draft.CharacteristicCode));
        MeasuredValue = draft.MeasuredValue;
        UomCode = MaintenanceText.Required(draft.UomCode, nameof(draft.UomCode));
        LowerSpecLimit = draft.LowerSpecLimit;
        UpperSpecLimit = draft.UpperSpecLimit;
        IsWithinSpec = (LowerSpecLimit is null || MeasuredValue >= LowerSpecLimit)
            && (UpperSpecLimit is null || MeasuredValue <= UpperSpecLimit);
    }

    public string CharacteristicCode { get; private set; } = string.Empty;
    public decimal MeasuredValue { get; private set; }
    public string UomCode { get; private set; } = string.Empty;
    public decimal? LowerSpecLimit { get; private set; }
    public decimal? UpperSpecLimit { get; private set; }
    public bool IsWithinSpec { get; private set; }

    public static MaintenanceInspectionMeasurement Create(MaintenanceInspectionMeasurementDraft draft)
    {
        return new MaintenanceInspectionMeasurement(draft);
    }
}
