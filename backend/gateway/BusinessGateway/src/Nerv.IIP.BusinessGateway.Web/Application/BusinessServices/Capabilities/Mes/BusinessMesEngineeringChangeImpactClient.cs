using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesEngineeringChangeImpactsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? ChangeNumber = null,
    string? WorkOrderId = null,
    int Skip = 0,
    int Take = 100,
    string? ScopeKind = null,
    string? ScopeId = null);

[JsonConverter(typeof(BusinessConsoleMesEngineeringChangeDispositionJsonConverter))]
public enum BusinessConsoleMesEngineeringChangeDisposition
{
    [EnumMember(Value = "pending-decision")]
    PendingDecision,
    [EnumMember(Value = "auto-rebound")]
    AutoRebound,
    [EnumMember(Value = "blocked-for-manual-confirmation")]
    BlockedForManualConfirmation,
    [EnumMember(Value = "continue-with-archived-version")]
    ContinueWithArchivedVersion,
    [EnumMember(Value = "abort-work-order")]
    AbortWorkOrder,
}

public sealed class BusinessConsoleMesEngineeringChangeDispositionJsonConverter()
    : JsonStringEnumConverter<BusinessConsoleMesEngineeringChangeDisposition>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false);

public sealed record BusinessConsoleMesEngineeringChangeImpact(
    [property: Required] string ChangeNumber,
    [property: Required] string WorkOrderId,
    [property: Required] BusinessConsoleMesEngineeringChangeDisposition Disposition,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

public sealed record BusinessConsoleMesEngineeringChangeImpactsResponse(
    [property: Required] IReadOnlyCollection<BusinessConsoleMesEngineeringChangeImpact> Items,
    [property: Required] int Total);

public sealed class HttpBusinessMesEngineeringChangeImpactClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient)
{
    internal async Task<BusinessConsoleMesEngineeringChangeImpactsResponse> ListAsync(
        string token,
        BusinessConsoleMesEngineeringChangeImpactsRequest request,
        PrincipalWorkScopeSelection scope,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<ImpactPage>(
            token, HttpMethod.Get, "/api/business/v1/mes/work-order-engineering-change-impacts?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("changeNumber", request.ChangeNumber),
                ("workOrderId", request.WorkOrderId),
                ("skip", request.Skip),
                ("take", request.Take),
                ("authorizedAssignedUserIds", Join(scope.AssignedUserIds)),
                ("authorizedTeamIds", Join(scope.TeamIds)),
                ("authorizedWorkCenterIds", Join(scope.WorkCenterIds))),
            null, cancellationToken, failClosedOnFailureEnvelope: true);

        if (result.Items is null || result.Total is null || result.Total < result.Items.Count)
        {
            throw InvalidResponse();
        }

        return new(result.Items.Select(item => MapImpact(item, request)).ToArray(), result.Total.Value);
    }

    private static BusinessConsoleMesEngineeringChangeImpact MapImpact(
        ImpactRow? item, BusinessConsoleMesEngineeringChangeImpactsRequest request)
    {
        if (item is null || !MatchesIdentity(item.ChangeNumber, request.ChangeNumber) ||
            !MatchesIdentity(item.WorkOrderId, request.WorkOrderId))
        {
            throw InvalidResponse();
        }

        var disposition = item.Disposition switch
        {
            "pending-decision" => BusinessConsoleMesEngineeringChangeDisposition.PendingDecision,
            "auto-rebound" => BusinessConsoleMesEngineeringChangeDisposition.AutoRebound,
            "blocked-for-manual-confirmation" => BusinessConsoleMesEngineeringChangeDisposition.BlockedForManualConfirmation,
            "continue-with-archived-version" => BusinessConsoleMesEngineeringChangeDisposition.ContinueWithArchivedVersion,
            "abort-work-order" => BusinessConsoleMesEngineeringChangeDisposition.AbortWorkOrder,
            _ => throw InvalidResponse(),
        };

        var isDecided = disposition is BusinessConsoleMesEngineeringChangeDisposition.ContinueWithArchivedVersion
            or BusinessConsoleMesEngineeringChangeDisposition.AbortWorkOrder;
        if (isDecided
            ? string.IsNullOrWhiteSpace(item.DecidedBy) || item.DecidedAtUtc is null || string.IsNullOrWhiteSpace(item.DecisionReason)
            : item.DecidedBy is not null || item.DecidedAtUtc is not null || item.DecisionReason is not null)
        {
            throw InvalidResponse();
        }

        return new(item.ChangeNumber!, item.WorkOrderId!, disposition, item.DecidedBy, item.DecidedAtUtc, item.DecisionReason);
    }

    private static bool MatchesIdentity(string? actual, string? requested) =>
        !string.IsNullOrWhiteSpace(actual) && actual == actual.Trim() && actual.Length <= 100 &&
        (requested is null || string.Equals(actual, requested, StringComparison.Ordinal));

    private static string Join(IReadOnlyCollection<string> values) => string.Join(',', values.Order(StringComparer.Ordinal));

    private static BusinessServiceProxyException InvalidResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "downstream-invalid-response");

    private sealed record ImpactPage(IReadOnlyCollection<ImpactRow?>? Items, int? Total);

    private sealed record ImpactRow(
        string? ChangeNumber, string? WorkOrderId, string? Disposition,
        string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason);
}
