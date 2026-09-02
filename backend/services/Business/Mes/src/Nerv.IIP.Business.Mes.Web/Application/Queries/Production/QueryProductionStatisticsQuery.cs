using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

[JsonConverter(typeof(ProductionStatisticsDimensionJsonConverter))]
public enum ProductionStatisticsDimension
{
    Day,
    Shift,
    WorkCenter,
    Sku,
}

public sealed class ProductionStatisticsDimensionJsonConverter()
    : JsonStringEnumConverter<ProductionStatisticsDimension>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(ProductionStatisticsResolutionStatusJsonConverter))]
public enum ProductionStatisticsResolutionStatus
{
    Resolved,
    Degraded,
}

public sealed class ProductionStatisticsResolutionStatusJsonConverter()
    : JsonStringEnumConverter<ProductionStatisticsResolutionStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(ProductionStatisticsDegradedReasonJsonConverter))]
public enum ProductionStatisticsDegradedReason
{
    HistoricalDimensionLegacyUnresolved,
    HistoricalTimezoneMissing,
    HistoricalTimezoneInvalid,
    HistoricalShiftDefinitionMissing,
    HistoricalShiftDefinitionInvalid,
    HistoricalReportOutsideShiftWindow,
    HistoricalLocalTimeInvalid,
    HistoricalLocalTimeAmbiguous,
    HistoricalDimensionSnapshotDegraded,
    WorkCenterMissing,
    NonPositiveTotalOutput,
}

public sealed class ProductionStatisticsDegradedReasonJsonConverter()
    : JsonStringEnumConverter<ProductionStatisticsDegradedReason>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record QueryProductionStatisticsQuery(
    string OrganizationId,
    string EnvironmentId,
    ProductionStatisticsDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateOnly? BusinessDate = null,
    string? ShiftCode = null,
    string? WorkCenterId = null,
    string? SkuId = null,
    int Skip = 0,
    int Take = 100) : IQuery<ProductionStatisticsResponse>;

public sealed class QueryProductionStatisticsQueryValidator : AbstractValidator<QueryProductionStatisticsQuery>
{
    public QueryProductionStatisticsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.ShiftCode).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.SkuId).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed record ProductionStatisticsResponse(
    string OrganizationId,
    string EnvironmentId,
    ProductionStatisticsDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<ProductionStatisticsBucket> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record ProductionStatisticsBucket(
    ProductionStatisticsDimension Dimension,
    string? DimensionValue,
    DateOnly? BusinessDate,
    string? ShiftCode,
    string? WorkCenterId,
    string? SkuId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    decimal TotalOutputQuantity,
    decimal? GoodRate,
    decimal? ScrapRate,
    decimal? ReworkRate,
    int ProductionReportCount,
    ProductionStatisticsResolutionStatus ResolutionStatus,
    IReadOnlyCollection<ProductionStatisticsDegradedReason> DegradedReasons);

public sealed class QueryProductionStatisticsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryProductionStatisticsQuery, ProductionStatisticsResponse>
{
    public async Task<ProductionStatisticsResponse> Handle(
        QueryProductionStatisticsQuery request,
        CancellationToken cancellationToken)
    {
        var scopedOriginalReportNos = dbContext.ProductionReports
            .AsNoTracking()
            .Where(report => report.OrganizationId == request.OrganizationId)
            .Where(report => report.EnvironmentId == request.EnvironmentId)
            .Where(report => report.ReversedReportNo == null)
            .Where(report => report.ReportedAtUtc >= request.WindowStartUtc)
            .Where(report => report.ReportedAtUtc < request.WindowEndUtc)
            .Select(report => report.ReportNo);
        var sourceRows = await (
            from report in dbContext.ProductionReports.AsNoTracking()
            join workOrder in dbContext.WorkOrders.AsNoTracking()
                on new { report.OrganizationId, report.EnvironmentId, WorkOrderId = report.WorkOrderId }
                equals new { workOrder.OrganizationId, workOrder.EnvironmentId, WorkOrderId = workOrder.WorkOrderIdValue }
            where report.OrganizationId == request.OrganizationId
                && report.EnvironmentId == request.EnvironmentId
                && ((report.ReversedReportNo == null
                        && report.ReportedAtUtc >= request.WindowStartUtc
                        && report.ReportedAtUtc < request.WindowEndUtc)
                    || (report.ReversedReportNo != null
                        && scopedOriginalReportNos.Contains(report.ReversedReportNo)))
            select new ProductionStatisticsSourceRow(
                report.ReportNo,
                report.ReversedReportNo,
                report.ReportedAtUtc,
                workOrder.SkuId,
                report.OeeWorkCenterId,
                report.OeeDimensionResolutionStatus,
                report.OeeSiteTimezone,
                report.OeeShiftCode,
                report.OeeShiftStartsAt,
                report.OeeShiftEndsAt,
                report.OeeShiftCrossesMidnight,
                report.OeeShiftPaidMinutes,
                report.OeeShiftBreakMinutes,
                report.GoodQuantity,
                report.ScrapQuantity,
                report.ReworkQuantity))
            .ToArrayAsync(cancellationToken);
        var originalsByReportNo = sourceRows
            .Where(row => row.ReversedReportNo is null)
            .ToDictionary(row => row.ReportNo, StringComparer.Ordinal);

        var shiftCode = Normalize(request.ShiftCode);
        var workCenterId = Normalize(request.WorkCenterId);
        var skuId = Normalize(request.SkuId);
        var facts = sourceRows
            .Select(row => Resolve(
                row,
                row.ReversedReportNo is null
                    ? row.ReportedAtUtc
                    : originalsByReportNo[row.ReversedReportNo].ReportedAtUtc))
            .Where(fact => request.BusinessDate is null || fact.BusinessDate == request.BusinessDate)
            .Where(fact => shiftCode is null || string.Equals(fact.ShiftCode, shiftCode, StringComparison.Ordinal))
            .Where(fact => workCenterId is null || string.Equals(fact.WorkCenterId, workCenterId, StringComparison.Ordinal))
            .Where(fact => skuId is null || string.Equals(fact.SkuId, skuId, StringComparison.Ordinal))
            .ToArray();
        var allItems = facts
            .GroupBy(fact => ProductionStatisticsBucketKey.From(fact, request.Dimension))
            .Select(group => Calculate(request.Dimension, group.Key, group.ToArray()))
            .OrderBy(item => item.DimensionValue is null)
            .ThenBy(item => item.DimensionValue, StringComparer.Ordinal)
            .ToArray();
        return new ProductionStatisticsResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.Dimension,
            request.WindowStartUtc,
            request.WindowEndUtc,
            allItems.Skip(request.Skip).Take(request.Take).ToArray(),
            allItems.Length,
            request.Skip,
            request.Take);
    }

    private static ProductionStatisticsFact Resolve(
        ProductionStatisticsSourceRow row,
        DateTimeOffset effectiveReportedAtUtc)
    {
        if (row.DimensionResolutionStatus is null)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalDimensionLegacyUnresolved);
        }

        if (row.SiteTimezone is null)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalTimezoneMissing);
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(row.SiteTimezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalTimezoneInvalid);
        }
        catch (InvalidTimeZoneException)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalTimezoneInvalid);
        }

        if (row.ShiftCode is null ||
            row.ShiftStartsAt is null ||
            row.ShiftEndsAt is null ||
            row.ShiftCrossesMidnight is null ||
            row.ShiftPaidMinutes is null ||
            row.ShiftBreakMinutes is null)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalShiftDefinitionMissing);
        }

        var startsAt = row.ShiftStartsAt.Value;
        var endsAt = row.ShiftEndsAt.Value;
        var crossesMidnight = row.ShiftCrossesMidnight.Value;
        if (startsAt == endsAt ||
            crossesMidnight != (endsAt <= startsAt) ||
            row.ShiftPaidMinutes <= 0 ||
            row.ShiftBreakMinutes < 0 ||
            row.ShiftBreakMinutes > row.ShiftPaidMinutes)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalShiftDefinitionInvalid);
        }

        var localReportedAt = TimeZoneInfo.ConvertTime(effectiveReportedAtUtc, timezone).DateTime;
        var localTime = TimeOnly.FromDateTime(localReportedAt);
        DateOnly? businessDate = crossesMidnight
            ? localTime >= startsAt
                ? DateOnly.FromDateTime(localReportedAt)
                : localTime < endsAt
                    ? DateOnly.FromDateTime(localReportedAt).AddDays(-1)
                    : null
            : localTime >= startsAt && localTime < endsAt
                ? DateOnly.FromDateTime(localReportedAt)
                : null;
        if (businessDate is null)
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalReportOutsideShiftWindow);
        }

        var localStart = DateTime.SpecifyKind(businessDate.Value.ToDateTime(startsAt), DateTimeKind.Unspecified);
        var endDate = crossesMidnight ? businessDate.Value.AddDays(1) : businessDate.Value;
        var localEnd = DateTime.SpecifyKind(endDate.ToDateTime(endsAt), DateTimeKind.Unspecified);
        if (timezone.IsInvalidTime(localStart) || timezone.IsInvalidTime(localEnd))
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalLocalTimeInvalid);
        }

        if (timezone.IsAmbiguousTime(localStart) || timezone.IsAmbiguousTime(localEnd))
        {
            return new(row, null, ProductionStatisticsDegradedReason.HistoricalLocalTimeAmbiguous);
        }

        return new(row, businessDate, null);
    }

    private static ProductionStatisticsBucket Calculate(
        ProductionStatisticsDimension dimension,
        ProductionStatisticsBucketKey key,
        IReadOnlyCollection<ProductionStatisticsFact> facts)
    {
        var goodQuantity = facts.Sum(x => x.Source.GoodQuantity);
        var scrapQuantity = facts.Sum(x => x.Source.ScrapQuantity);
        var reworkQuantity = facts.Sum(x => x.Source.ReworkQuantity);
        var totalOutputQuantity = goodQuantity + scrapQuantity + reworkQuantity;
        var degradedReasons = new HashSet<ProductionStatisticsDegradedReason>();
        foreach (var fact in facts)
        {
            var dimensionReason = DimensionReason(fact, dimension);
            if (dimensionReason is not null)
            {
                degradedReasons.Add(dimensionReason.Value);
            }
        }

        if (totalOutputQuantity <= 0m)
        {
            degradedReasons.Add(ProductionStatisticsDegradedReason.NonPositiveTotalOutput);
        }

        return new ProductionStatisticsBucket(
            dimension,
            key.DimensionValue,
            key.BusinessDate,
            key.ShiftCode,
            key.WorkCenterId,
            key.SkuId,
            Round(goodQuantity),
            Round(scrapQuantity),
            Round(reworkQuantity),
            Round(totalOutputQuantity),
            Rate(goodQuantity, totalOutputQuantity),
            Rate(scrapQuantity, totalOutputQuantity),
            Rate(reworkQuantity, totalOutputQuantity),
            facts.Count,
            degradedReasons.Count == 0
                ? ProductionStatisticsResolutionStatus.Resolved
                : ProductionStatisticsResolutionStatus.Degraded,
            degradedReasons.OrderBy(x => x).ToArray());
    }

    private static decimal? Rate(decimal numerator, decimal denominator) =>
        denominator > 0m ? Round(decimal.Divide(numerator, denominator)) : null;

    private static decimal Round(decimal value) => Math.Round(value, 6);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProductionStatisticsDegradedReason? DimensionReason(
        ProductionStatisticsFact fact,
        ProductionStatisticsDimension dimension) =>
        dimension switch
        {
            ProductionStatisticsDimension.Day or ProductionStatisticsDimension.Shift => fact.HistoricalResolutionReason,
            ProductionStatisticsDimension.WorkCenter when fact.WorkCenterId is null =>
                ProductionStatisticsDegradedReason.WorkCenterMissing,
            ProductionStatisticsDimension.WorkCenter when fact.Source.DimensionResolutionStatus is null =>
                ProductionStatisticsDegradedReason.HistoricalDimensionLegacyUnresolved,
            ProductionStatisticsDimension.WorkCenter when !string.Equals(
                fact.Source.DimensionResolutionStatus,
                "resolved",
                StringComparison.OrdinalIgnoreCase) =>
                ProductionStatisticsDegradedReason.HistoricalDimensionSnapshotDegraded,
            _ => null,
        };

    private sealed record ProductionStatisticsSourceRow(
        string ReportNo,
        string? ReversedReportNo,
        DateTimeOffset ReportedAtUtc,
        string SkuId,
        string? WorkCenterId,
        string? DimensionResolutionStatus,
        string? SiteTimezone,
        string? ShiftCode,
        TimeOnly? ShiftStartsAt,
        TimeOnly? ShiftEndsAt,
        bool? ShiftCrossesMidnight,
        int? ShiftPaidMinutes,
        int? ShiftBreakMinutes,
        decimal GoodQuantity,
        decimal ScrapQuantity,
        decimal ReworkQuantity);

    private sealed record ProductionStatisticsFact(
        ProductionStatisticsSourceRow Source,
        DateOnly? BusinessDate,
        ProductionStatisticsDegradedReason? HistoricalResolutionReason)
    {
        public string? ShiftCode => Source.ShiftCode;
        public string? WorkCenterId => Source.WorkCenterId;
        public string SkuId => Source.SkuId;
    }

    private sealed record ProductionStatisticsBucketKey(
        string? DimensionValue,
        DateOnly? BusinessDate,
        string? ShiftCode,
        string? WorkCenterId,
        string? SkuId)
    {
        public static ProductionStatisticsBucketKey From(
            ProductionStatisticsFact fact,
            ProductionStatisticsDimension dimension) =>
            dimension switch
            {
                ProductionStatisticsDimension.Day => new(
                    fact.BusinessDate?.ToString("yyyy-MM-dd"),
                    fact.BusinessDate,
                    null,
                    null,
                    null),
                ProductionStatisticsDimension.Shift => new(
                    fact.BusinessDate is null || fact.ShiftCode is null
                        ? null
                        : $"{fact.BusinessDate:yyyy-MM-dd}/{fact.ShiftCode}",
                    fact.BusinessDate,
                    fact.ShiftCode,
                    null,
                    null),
                ProductionStatisticsDimension.WorkCenter => new(
                    fact.WorkCenterId,
                    null,
                    null,
                    fact.WorkCenterId,
                    null),
                ProductionStatisticsDimension.Sku => new(
                    fact.SkuId,
                    null,
                    null,
                    null,
                    fact.SkuId),
                _ => throw new UnreachableException(),
            };
    }
}
