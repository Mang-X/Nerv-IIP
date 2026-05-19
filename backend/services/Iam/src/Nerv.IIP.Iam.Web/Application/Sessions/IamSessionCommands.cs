using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Sessions;

public sealed record RevokeSessionCommand(string SessionId) : ICommand;

public sealed class RevokeSessionCommandHandler(IIamSessionApplicationService sessions)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        await sessions.RevokeSessionAsync(request.SessionId, cancellationToken);
    }
}
