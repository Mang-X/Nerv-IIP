using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Commands.Users;
using Nerv.IIP.Iam.Web.Application.Queries.Users;
using Nerv.IIP.Iam.Web.Application.Users;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.Users;

public sealed record CreateUserRequest(string LoginName, string Email, string Password, DateTimeOffset? AccountExpiresAtUtc);
public sealed record UpdateUserRequest(string LoginName, string Email, bool Enabled, DateTimeOffset? AccountExpiresAtUtc);
public sealed record ResetUserPasswordRequest(string NewPassword);
public sealed record ListUsersRequest(
    int? PageIndex,
    int? PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch,
    bool? FilterEnabled);

public sealed record WorkerDirectoryUserResponse(
    string UserId,
    string DisplayName,
    string? EmployeeNo,
    string? Department,
    string Status,
    string? Email);

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

[HttpGet("/internal/iam/v1/workers")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListWorkerDirectoryEndpoint(IMediator mediator)
    : Endpoint<ListUsersRequest, ResponseData<PagedListResponse<WorkerDirectoryUserResponse>>>
{
    public override async Task HandleAsync(ListUsersRequest req, CancellationToken ct)
    {
        var users = await mediator.Send(new ListUsersQuery(IamListQueryOptions.Create(
            req.PageIndex,
            req.PageSize,
            req.SortBy,
            req.SortOrder,
            req.FilterSearch,
            filterEnabled: req.FilterEnabled)), ct);

        var response = new PagedListResponse<WorkerDirectoryUserResponse>(
            users.PageIndex,
            users.PageSize,
            users.TotalCount,
            users.Items.Select(ToWorker).ToArray());
        await Send.OkAsync(response.AsResponseData(), ct);
    }

    private static WorkerDirectoryUserResponse ToWorker(UserResponse user)
    {
        var status = user.Enabled ? "active" : "disabled";
        return new WorkerDirectoryUserResponse(
            user.UserId,
            // TODO: Replace this fallback once IAM stores worker profile display names and employee numbers.
            user.LoginName,
            null,
            null,
            status,
            user.Email);
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
        var response = await mediator.Send(new CreateUserCommand(req.LoginName, req.Email, req.Password, req.AccountExpiresAtUtc), ct);
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
        var response = await mediator.Send(new UpdateUserCommand(userId, req.LoginName, req.Email, req.Enabled, req.AccountExpiresAtUtc), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpPost("/api/iam/v1/users/{userId}/enable")]
[AllowAnonymous]
public sealed class EnableUserEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var userId = Route<string>("userId") ?? string.Empty;
        await mediator.Send(new EnableUserCommand(userId), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
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
