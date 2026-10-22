using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record UpdateUserCommand(
    string UserId,
    string LoginName,
    string Email,
    bool Enabled,
    DateTimeOffset? AccountExpiresAtUtc) : ICommand<UserResponse>;

public sealed class UpdateUserCommandHandler(IIamUserApplicationService users, IOptions<IamSeedOptions> seed)
    : ICommandHandler<UpdateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        PlatformAdministratorProtection.EnsureCanUpdate(seed.Value, request.UserId, request.Enabled, request.AccountExpiresAtUtc);
        return await users.UpdateUserAsync(
            request.UserId,
            request.LoginName,
            request.Email,
            request.Enabled,
            request.AccountExpiresAtUtc,
            cancellationToken);
    }
}
