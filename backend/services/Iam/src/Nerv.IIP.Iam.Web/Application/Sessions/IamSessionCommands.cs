using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;

namespace Nerv.IIP.Iam.Web.Application.Sessions;

public sealed record RevokeSessionCommand(string SessionId, SecurityAuditContext? AuditContext) : ICommand;

public sealed class RevokeSessionCommandHandler(IIamSessionApplicationService sessions)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        await sessions.RevokeSessionAsync(request.SessionId, request.AuditContext, cancellationToken);
    }
}
