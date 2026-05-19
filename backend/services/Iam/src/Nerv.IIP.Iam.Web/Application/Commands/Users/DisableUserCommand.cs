using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record DisableUserCommand(string UserId) : ICommand;

public sealed class DisableUserCommandHandler(IIamUserApplicationService users)
    : ICommandHandler<DisableUserCommand>
{
    public async Task Handle(DisableUserCommand request, CancellationToken cancellationToken)
    {
        await users.DisableUserAsync(request.UserId, cancellationToken);
    }
}
