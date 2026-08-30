using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.IndustrialTelemetry;

[JsonConverter(typeof(OeeAggregateDimensionJsonConverter))]
public enum OeeAggregateDimension
{
    [EnumMember(Value = "device")]
    Device,
    [EnumMember(Value = "workCenter")]
    WorkCenter,
    [EnumMember(Value = "line")]
    Line,
    [EnumMember(Value = "workshop")]
    Workshop,
    [EnumMember(Value = "shift")]
    Shift,
    [EnumMember(Value = "day")]
    Day,
}

public sealed class OeeAggregateDimensionJsonConverter()
    : JsonStringEnumConverter<OeeAggregateDimension>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(OeeAggregateDegradedReasonJsonConverter))]
public enum OeeAggregateDegradedReason
{
    [EnumMember(Value = "runtimeStateFactsMissing")]
    RuntimeStateFactsMissing,
    [EnumMember(Value = "runtimeStateCoverageIncomplete")]
    RuntimeStateCoverageIncomplete,
    [EnumMember(Value = "productionUomAmbiguous")]
    ProductionUomAmbiguous,
    [EnumMember(Value = "productionOutputMissing")]
    ProductionOutputMissing,
    [EnumMember(Value = "theoreticalRateMissingOrAmbiguous")]
    TheoreticalRateMissingOrAmbiguous,
    [EnumMember(Value = "productiveRuntimeMissing")]
    ProductiveRuntimeMissing,
    [EnumMember(Value = "loadingRuntimeMissing")]
    LoadingRuntimeMissing,
    [EnumMember(Value = "historicalDimensionLegacyUnresolved")]
    HistoricalDimensionLegacyUnresolved,
    [EnumMember(Value = "historicalHierarchyMissing")]
    HistoricalHierarchyMissing,
    [EnumMember(Value = "historicalTimezoneMissing")]
    HistoricalTimezoneMissing,
    [EnumMember(Value = "historicalTimezoneInvalid")]
    HistoricalTimezoneInvalid,
    [EnumMember(Value = "historicalShiftDefinitionMissing")]
    HistoricalShiftDefinitionMissing,
    [EnumMember(Value = "historicalShiftDefinitionInvalid")]
    HistoricalShiftDefinitionInvalid,
    [EnumMember(Value = "historicalReportOutsideShiftWindow")]
    HistoricalReportOutsideShiftWindow,
    [EnumMember(Value = "historicalLocalTimeInvalid")]
    HistoricalLocalTimeInvalid,
    [EnumMember(Value = "historicalLocalTimeAmbiguous")]
    HistoricalLocalTimeAmbiguous,
    [EnumMember(Value = "siteDimensionMissing")]
    SiteDimensionMissing,
    [EnumMember(Value = "workshopDimensionMissing")]
    WorkshopDimensionMissing,
    [EnumMember(Value = "lineDimensionMissing")]
    LineDimensionMissing,
    [EnumMember(Value = "siteDimensionAmbiguous")]
    SiteDimensionAmbiguous,
    [EnumMember(Value = "workshopDimensionAmbiguous")]
    WorkshopDimensionAmbiguous,
    [EnumMember(Value = "lineDimensionAmbiguous")]
    LineDimensionAmbiguous,
    [EnumMember(Value = "siteTimezoneOrDayBoundaryMissing")]
    SiteTimezoneOrDayBoundaryMissing,
    [EnumMember(Value = "shiftDefinitionOrBoundaryMissing")]
    ShiftDefinitionOrBoundaryMissing,
}

public sealed class OeeAggregateDegradedReasonJsonConverter()
    : JsonStringEnumConverter<OeeAggregateDegradedReason>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record QueryOeeAggregateBucketsRequest(
    string OrganizationId,
    string EnvironmentId,
    OeeAggregateDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetId = null,
    string? WorkCenterId = null,
    string? ShiftCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    DateOnly? BusinessDate = null,
    int Skip = 0,
    int Take = 100);

public sealed record OeeAggregateBucketsResponse(
    string OrganizationId,
    string EnvironmentId,
    OeeAggregateDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<OeeAggregateBucket> Buckets,
    int TotalCount,
    int Skip,
    int Take);

public sealed record OeeAggregateBucket(
    OeeAggregateDimension Dimension,
    string? DimensionValue,
    string? SiteCode,
    string? WorkshopCode,
    string? LineCode,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? ShiftCode,
    DateOnly? BusinessDate,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int DeviceCount,
    int StateSampleCount,
    int ProductionFactCount,
    decimal? AvailabilityRate,
    decimal? PerformanceRate,
    decimal? QualityRate,
    decimal? OeeRate,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    string? OutputUomCode,
    decimal? ExpectedOutputQuantity,
    bool IsDegraded,
    IReadOnlyCollection<OeeAggregateDegradedReason> DegradedReasons);
