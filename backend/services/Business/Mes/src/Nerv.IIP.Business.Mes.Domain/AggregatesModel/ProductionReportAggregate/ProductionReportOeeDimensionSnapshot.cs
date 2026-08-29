namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public sealed record ProductionReportOeeDimensionSnapshot(
    string ResolutionStatus,
    string? DegradedReason,
    string? DeviceAssetId,
    string WorkCenterId,
    string? SiteCode,
    string? WorkshopCode,
    string? LineCode,
    string? SiteTimezone,
    string? ShiftCode,
    TimeOnly? ShiftStartsAt,
    TimeOnly? ShiftEndsAt,
    bool? ShiftCrossesMidnight,
    int? ShiftPaidMinutes,
    int? ShiftBreakMinutes)
{
    public static ProductionReportOeeDimensionSnapshot Resolved(
        string deviceAssetId,
        string workCenterId,
        string siteCode,
        string? workshopCode,
        string? lineCode,
        string? siteTimezone,
        string? shiftCode,
        TimeOnly? shiftStartsAt,
        TimeOnly? shiftEndsAt,
        bool? shiftCrossesMidnight,
        int? shiftPaidMinutes,
        int? shiftBreakMinutes) =>
        new(
            "resolved",
            null,
            deviceAssetId,
            workCenterId,
            siteCode,
            workshopCode,
            lineCode,
            siteTimezone,
            shiftCode,
            shiftStartsAt,
            shiftEndsAt,
            shiftCrossesMidnight,
            shiftPaidMinutes,
            shiftBreakMinutes);

    public static ProductionReportOeeDimensionSnapshot Degraded(
        string? rawDeviceAssetReference,
        string workCenterId,
        string reason) =>
        new(
            "degraded",
            reason,
            string.IsNullOrWhiteSpace(rawDeviceAssetReference) ? null : rawDeviceAssetReference.Trim(),
            workCenterId,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
}
