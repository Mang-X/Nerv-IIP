using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record EnableUserCommand(string UserId) : ICommand;

public sealed class EnableUserCommandHandler(IIamUserApplicationService users)
    : ICommandHandler<EnableUserCommand>
{
    public async Task Handle(EnableUserCommand request, CancellationToken cancellationToken)
    {
        await users.EnableUserAsync(request.UserId, cancellationToken);
    }
}
