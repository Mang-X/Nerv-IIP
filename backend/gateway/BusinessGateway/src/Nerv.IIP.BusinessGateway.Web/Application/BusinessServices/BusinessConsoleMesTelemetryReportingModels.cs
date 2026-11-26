using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesTelemetryCandidateListRequest(string OrganizationId, string EnvironmentId, string? Status = null,
    string? WorkCenterId = null, string? DeviceAssetId = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, int Skip = 0, int Take = 50);
public sealed record BusinessConsoleMesTelemetryCandidateDetailRequest([property: RouteParam] string CandidateId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId);
public sealed record BusinessConsoleMesTelemetryCandidatePromoteRequest([property: RouteParam] string CandidateId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId, string WorkOrderId, string OperationTaskId);
public sealed record BusinessConsoleMesTelemetryCandidateDismissRequest([property: RouteParam] string CandidateId,
    [property: QueryParam] string OrganizationId, [property: QueryParam] string EnvironmentId, string Reason);
public sealed record BusinessConsoleMesTelemetryCandidateListResponse(IReadOnlyCollection<BusinessConsoleMesTelemetryCandidateRow> Items, int Total);
public sealed record BusinessConsoleMesTelemetryCandidateTransition(string FromStatus, string ToStatus, string Actor, string? Reason, DateTimeOffset OccurredAtUtc);
public sealed record BusinessConsoleMesTelemetryCandidateRow(string CandidateId, string OrganizationId, string EnvironmentId, string Status,
    string ReportingMode, string DeviceAssetId, string TagKey, decimal GoodQuantity, DateTimeOffset BucketStartUtc, DateTimeOffset BucketEndUtc,
    string? WorkCenterId, string? WorkOrderId, string? OperationTaskId, string? SuspensionReason, string SourceIdempotencyKey,
    string? ResolutionReason, string? ResolvedBy, DateTimeOffset? ResolvedAtUtc, string? ProductionReportId,
    IReadOnlyCollection<BusinessConsoleMesTelemetryCandidateTransition> Transitions);
