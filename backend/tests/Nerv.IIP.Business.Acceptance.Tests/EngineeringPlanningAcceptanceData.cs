using System.Reflection;

namespace Nerv.IIP.Business.Acceptance.Tests;

public static class EngineeringPlanningAcceptanceData
{
    public const string SkuCode = "SKU-FG-ACCEPT-001";
    public const string WorkCenterCode = "WC-ASSY-ACCEPT-001";
    public const string MbomVersionId = "MBOM-ACCEPT-001";
    public const string RoutingVersionId = "ROUTING-ACCEPT-001";
    public const string ProductionVersionId = "PV-ACCEPT-001";
    public const string DemandSourceReference = "SO-DEMAND-ACCEPT-001";
    public const string PlannedPurchaseSuggestionId = "SUG-PUR-ACCEPT-001";
    public const string PlannedWorkOrderSuggestionId = "SUG-WO-ACCEPT-001";
    public const string PurchaseRequisitionNo = "PR-ACCEPT-001";
    public const string WorkOrderId = "WO-ACCEPT-001";

    public static IReadOnlyDictionary<string, string> VisibleFacts(BusinessAcceptanceRecordedEvent recordedEvent)
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);
        ArgumentNullException.ThrowIfNull(recordedEvent.Payload);

        return recordedEvent.Payload
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(
                property => property.Name,
                property => Convert.ToString(property.GetValue(recordedEvent.Payload), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);
    }
}
