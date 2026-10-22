using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;

internal static class OeeHistoricalDimensionResolver
{
    public static OeeHistoricalDimensionSnapshot Resolve(ProductionReportRecordedPayload payload)
    {
        var siteCode = Optional(payload.SiteCode);
        var workshopCode = Optional(payload.WorkshopCode);
        var lineCode = Optional(payload.LineCode);
        var shiftCode = Optional(payload.ShiftCode);
        var siteTimezone = Optional(payload.SiteTimezone);
        var hierarchyResolved = siteCode != null && workshopCode != null && lineCode != null;

        if (siteTimezone == null)
        {
            return Snapshot(OeeHistoricalDimensionStatus.MissingTimezone);
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(siteTimezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return Snapshot(OeeHistoricalDimensionStatus.InvalidTimezone);
        }
        catch (InvalidTimeZoneException)
        {
            return Snapshot(OeeHistoricalDimensionStatus.InvalidTimezone);
        }

        if (shiftCode == null ||
            payload.ShiftStartsAt == null ||
            payload.ShiftEndsAt == null ||
            payload.ShiftCrossesMidnight == null ||
            payload.ShiftPaidMinutes == null ||
            payload.ShiftBreakMinutes == null)
        {
            return Snapshot(OeeHistoricalDimensionStatus.MissingShiftDefinition);
        }

        var startsAt = payload.ShiftStartsAt.Value;
        var endsAt = payload.ShiftEndsAt.Value;
        var crossesMidnight = payload.ShiftCrossesMidnight.Value;
        if (startsAt == endsAt ||
            crossesMidnight != (endsAt <= startsAt) ||
            payload.ShiftPaidMinutes <= 0 ||
            payload.ShiftBreakMinutes < 0 ||
            payload.ShiftBreakMinutes > payload.ShiftPaidMinutes)
        {
            return Snapshot(OeeHistoricalDimensionStatus.InvalidShiftDefinition);
        }

        var localReportedAt = TimeZoneInfo.ConvertTime(payload.ReportedAtUtc, timezone).DateTime;
        var localTime = TimeOnly.FromDateTime(localReportedAt);
        DateOnly businessDate;
        if (crossesMidnight)
        {
            if (localTime >= startsAt)
            {
                businessDate = DateOnly.FromDateTime(localReportedAt);
            }
            else if (localTime < endsAt)
            {
                businessDate = DateOnly.FromDateTime(localReportedAt).AddDays(-1);
            }
            else
            {
                return Snapshot(OeeHistoricalDimensionStatus.ReportOutsideShiftWindow);
            }
        }
        else if (localTime >= startsAt && localTime < endsAt)
        {
            businessDate = DateOnly.FromDateTime(localReportedAt);
        }
        else
        {
            return Snapshot(OeeHistoricalDimensionStatus.ReportOutsideShiftWindow);
        }

        var localStart = DateTime.SpecifyKind(businessDate.ToDateTime(startsAt), DateTimeKind.Unspecified);
        var endDate = crossesMidnight ? businessDate.AddDays(1) : businessDate;
        var localEnd = DateTime.SpecifyKind(endDate.ToDateTime(endsAt), DateTimeKind.Unspecified);
        if (timezone.IsInvalidTime(localStart) || timezone.IsInvalidTime(localEnd))
        {
            return Snapshot(OeeHistoricalDimensionStatus.InvalidLocalTime);
        }

        if (timezone.IsAmbiguousTime(localStart) || timezone.IsAmbiguousTime(localEnd))
        {
            return Snapshot(OeeHistoricalDimensionStatus.AmbiguousLocalTime);
        }

        var bucketStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timezone), TimeSpan.Zero);
        var bucketEndUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone), TimeSpan.Zero);
        return new OeeHistoricalDimensionSnapshot(
            siteCode,
            workshopCode,
            lineCode,
            shiftCode,
            siteTimezone,
            startsAt,
            endsAt,
            crossesMidnight,
            payload.ShiftPaidMinutes,
            payload.ShiftBreakMinutes,
            businessDate,
            bucketStartUtc,
            bucketEndUtc,
            hierarchyResolved ? OeeHistoricalDimensionStatus.Resolved : OeeHistoricalDimensionStatus.MissingHierarchy);

        OeeHistoricalDimensionSnapshot Snapshot(OeeHistoricalDimensionStatus status) => new(
            siteCode,
            workshopCode,
            lineCode,
            shiftCode,
            siteTimezone,
            payload.ShiftStartsAt,
            payload.ShiftEndsAt,
            payload.ShiftCrossesMidnight,
            payload.ShiftPaidMinutes,
            payload.ShiftBreakMinutes,
            null,
            null,
            null,
            status);
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
