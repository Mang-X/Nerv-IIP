using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Iam.Web.Application.Sessions;

namespace Nerv.IIP.Iam.Web.Endpoints.Sessions;

public sealed record ListSessionsRequest(
    int? PageIndex,
    int? PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch,
    bool? FilterRevoked);

[HttpGet("/api/iam/v1/sessions")]
[AllowAnonymous]
public sealed class ListSessionsEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamSessionApplicationService sessions) : Endpoint<ListSessionsRequest, PagedListResponse<SessionResponse>>
{
    public override async Task HandleAsync(ListSessionsRequest req, CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.sessions.read", ct))
        {
            return;
        }

        var response = await sessions.ListSessionsAsync(IamListQueryOptions.Create(
            req.PageIndex,
            req.PageSize,
            req.SortBy,
            req.SortOrder,
            req.FilterSearch,
            filterRevoked: req.FilterRevoked), ct);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

[HttpPost("/api/iam/v1/sessions/{sessionId}/revoke")]
[AllowAnonymous]
public sealed class RevokeSessionEndpoint(
    IIamPermissionAuthorizer authorizer,
    IMediator mediator) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.sessions.revoke", ct))
        {
            return;
        }

        await mediator.Send(new RevokeSessionCommand(Route<string>("sessionId")!), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
