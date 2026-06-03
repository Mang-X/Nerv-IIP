using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleSearchRequest(
    [property: QueryParam] string Q,
    [property: QueryParam] string? Types = null,
    [property: QueryParam] int Take = 20);

public sealed record BusinessConsoleSearchResponse(
    string Query,
    int Take,
    string MatchScope,
    string MatchScopeDescription,
    IReadOnlyCollection<BusinessConsoleSearchResult> Results,
    IReadOnlyCollection<BusinessConsoleSearchSourceStatus> SourceStatuses,
    IReadOnlyCollection<BusinessConsoleSearchTypeStatus> TypeStatuses);

public sealed record BusinessConsoleSearchResult(
    string ObjectType,
    string Title,
    string ObjectNumber,
    string Route,
    string Reference,
    string Summary);

public sealed record BusinessConsoleSearchSourceStatus(
    string Source,
    string Status,
    string? PermissionCode,
    string? Reason);

public sealed record BusinessConsoleSearchTypeStatus(
    string ObjectType,
    string Source,
    string Status,
    string? PermissionCode,
    string? Reason);
