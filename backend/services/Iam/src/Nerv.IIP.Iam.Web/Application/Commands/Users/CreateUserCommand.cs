using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record CreateUserCommand(
    string LoginName,
    string Email,
    string Password,
    DateTimeOffset? AccountExpiresAtUtc) : ICommand<UserResponse>;

public sealed class CreateUserCommandHandler(IIamUserApplicationService users)
    : ICommandHandler<CreateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        return await users.CreateUserAsync(
            request.LoginName,
            request.Email,
            request.Password,
            request.AccountExpiresAtUtc,
            cancellationToken);
    }
}
