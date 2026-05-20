using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record ResetUserPasswordCommand(string UserId, string NewPassword) : ICommand;

public sealed class ResetUserPasswordCommandHandler(IIamUserApplicationService users)
    : ICommandHandler<ResetUserPasswordCommand>
{
    public async Task Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(request.UserId, request.NewPassword, cancellationToken);
    }
}
