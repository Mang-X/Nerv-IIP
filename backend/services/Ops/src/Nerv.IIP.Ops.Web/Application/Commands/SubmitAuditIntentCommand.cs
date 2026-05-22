using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record SubmitAuditIntentCommand(SubmitAuditIntentRequest Request, DateTimeOffset Now) : ICommand<AuditIntentResponse>;

public sealed class SubmitAuditIntentCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<SubmitAuditIntentCommand, AuditIntentResponse>
{
    public async Task<AuditIntentResponse> Handle(SubmitAuditIntentCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.SubmitAuditIntentAsync(request.Request, request.Now, cancellationToken);
    }
}
