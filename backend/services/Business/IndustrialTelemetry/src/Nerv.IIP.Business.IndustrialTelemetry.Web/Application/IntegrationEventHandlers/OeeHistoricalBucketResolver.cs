using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.IntegrationEventHandlers;

internal static class OeeHistoricalBucketResolver
{
    public static OeeHistoricalDimensionSnapshot Resolve(ProductionReportRecordedPayload payload)
    {
        var snapshot = new OeeHistoricalDimensionSnapshot(
            payload.SiteCode,
            payload.WorkshopCode,
            payload.LineCode,
            payload.ShiftCode,
            payload.SiteTimezone,
            payload.ShiftStartsAt,
            payload.ShiftEndsAt,
            payload.ShiftCrossesMidnight,
            payload.ShiftPaidMinutes,
            payload.ShiftBreakMinutes);
        if (string.IsNullOrWhiteSpace(payload.SiteTimezone))
        {
            return snapshot;
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(payload.SiteTimezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return snapshot;
        }
        catch (InvalidTimeZoneException)
        {
            return snapshot;
        }

        var localReportedAt = TimeZoneInfo.ConvertTime(payload.ReportedAtUtc, timezone);
        var businessDate = DateOnly.FromDateTime(localReportedAt.DateTime);
        var dayStartUtc = ToUtc(businessDate, TimeOnly.MinValue, timezone);
        var dayEndUtc = ToUtc(businessDate.AddDays(1), TimeOnly.MinValue, timezone);
        if (dayStartUtc is null || dayEndUtc is null)
        {
            return snapshot;
        }

        var withDay = snapshot with
        {
            BusinessDate = businessDate,
            DayBucketStartUtc = dayStartUtc,
            DayBucketEndUtc = dayEndUtc,
        };
        if (string.IsNullOrWhiteSpace(payload.ShiftCode) ||
            payload.ShiftStartsAt is null ||
            payload.ShiftEndsAt is null ||
            payload.ShiftCrossesMidnight is null)
        {
            return withDay;
        }

        var startsAt = payload.ShiftStartsAt.Value;
        var endsAt = payload.ShiftEndsAt.Value;
        var crossesMidnight = payload.ShiftCrossesMidnight.Value;
        if (startsAt == endsAt || crossesMidnight != (endsAt <= startsAt))
        {
            return withDay;
        }

        var localTime = TimeOnly.FromDateTime(localReportedAt.DateTime);
        DateOnly? shiftBusinessDate = crossesMidnight switch
        {
            true when localTime >= startsAt => businessDate,
            true when localTime < endsAt => businessDate.AddDays(-1),
            false when localTime >= startsAt && localTime < endsAt => businessDate,
            _ => null,
        };
        if (shiftBusinessDate is null)
        {
            return withDay;
        }

        var shiftEndDate = crossesMidnight ? shiftBusinessDate.Value.AddDays(1) : shiftBusinessDate.Value;
        var shiftStartUtc = ToUtc(shiftBusinessDate.Value, startsAt, timezone);
        var shiftEndUtc = ToUtc(shiftEndDate, endsAt, timezone);
        if (shiftStartUtc is null || shiftEndUtc is null || shiftEndUtc <= shiftStartUtc)
        {
            return withDay;
        }

        return withDay with
        {
            ShiftBusinessDate = shiftBusinessDate,
            ShiftBucketStartUtc = shiftStartUtc,
            ShiftBucketEndUtc = shiftEndUtc,
        };
    }

    private static DateTimeOffset? ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timezone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        if (timezone.IsInvalidTime(local) || timezone.IsAmbiguousTime(local))
        {
            return null;
        }

        return new DateTimeOffset(local, timezone.GetUtcOffset(local)).ToUniversalTime();
    }
}
