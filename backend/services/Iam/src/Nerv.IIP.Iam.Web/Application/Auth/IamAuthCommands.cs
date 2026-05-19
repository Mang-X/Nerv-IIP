using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed record AuthCommandResult<T>(bool IsAuthorized, T? Response, string? Detail)
{
    public static AuthCommandResult<T> Authorized(T response) => new(true, response, null);
    public static AuthCommandResult<T> Unauthorized(string detail) => new(false, default, detail);
}

public sealed record LoginCommand(
    string LoginName,
    string Password,
    string? ClientInfo,
    string? IpAddress) : ICommand<AuthCommandResult<AuthResponse>>;

public sealed class LoginCommandHandler(IIamAuthService auth)
    : ICommandHandler<LoginCommand, AuthCommandResult<AuthResponse>>
{
    public async Task<AuthCommandResult<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await auth.LoginAsync(
                request.LoginName,
                request.Password,
                request.ClientInfo,
                request.IpAddress,
                cancellationToken);
            return AuthCommandResult<AuthResponse>.Authorized(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return AuthCommandResult<AuthResponse>.Unauthorized(ex.Message);
        }
    }
}

public sealed record RefreshCommand(
    string RefreshToken,
    string? ClientInfo,
    string? IpAddress) : ICommand<AuthCommandResult<AuthResponse>>;

public sealed class RefreshCommandHandler(IIamAuthService auth)
    : ICommandHandler<RefreshCommand, AuthCommandResult<AuthResponse>>
{
    public async Task<AuthCommandResult<AuthResponse>> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await auth.RefreshAsync(
                request.RefreshToken,
                request.ClientInfo,
                request.IpAddress,
                cancellationToken);
            return AuthCommandResult<AuthResponse>.Authorized(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return AuthCommandResult<AuthResponse>.Unauthorized(ex.Message);
        }
    }
}

public sealed record LogoutCommand(string SessionId) : ICommand;

public sealed class LogoutCommandHandler(IIamAuthService auth)
    : ICommandHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await auth.RevokeSessionAsync(request.SessionId, "logout", cancellationToken);
    }
}
