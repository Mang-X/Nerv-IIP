using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Commands.Users;
using Nerv.IIP.Iam.Web.Application.Queries.Users;
using Nerv.IIP.Iam.Web.Application.Users;
using Nerv.IIP.Iam.Web.Endpoints;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Users;

public sealed record CreateUserRequest(string LoginName, string Email, string Password);
public sealed record UpdateUserRequest(string LoginName, string Email, bool Enabled);
public sealed record ResetUserPasswordRequest(string NewPassword);
public sealed record ListUsersRequest(
    int? PageIndex,
    int? PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch,
    bool? FilterEnabled);

[HttpGet("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class ListUsersEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : Endpoint<ListUsersRequest, ResponseData<PagedListResponse<UserResponse>>>
{
    public override async Task HandleAsync(ListUsersRequest req, CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.read", ct))
        {
            return;
        }

        var users = await mediator.Send(new ListUsersQuery(IamListQueryOptions.Create(
            req.PageIndex,
            req.PageSize,
            req.SortBy,
            req.SortOrder,
            req.FilterSearch,
            filterEnabled: req.FilterEnabled)), ct);
        await Send.OkAsync(users.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/users")]
[AllowAnonymous]
public sealed class CreateUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest<ResponseData<UserResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<CreateUserRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var response = await mediator.Send(new CreateUserCommand(req.LoginName, req.Email, req.Password), ct);
        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
    }
}

[HttpPatch("/api/iam/v1/users/{userId}")]
[AllowAnonymous]
public sealed class PatchUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest<ResponseData<UserResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<UpdateUserRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var userId = Route<string>("userId") ?? string.Empty;
        var response = await mediator.Send(new UpdateUserCommand(userId, req.LoginName, req.Email, req.Enabled), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/users/{userId}/disable")]
[AllowAnonymous]
public sealed class DisableUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new DisableUserCommand(userId), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

[HttpPost("/api/iam/v1/users/{userId}/reset-password")]
[AllowAnonymous]
public sealed class ResetUserPasswordEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<ResetUserPasswordRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new ResetUserPasswordCommand(userId, SensitivePassword.From(req.NewPassword)), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
