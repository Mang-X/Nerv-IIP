using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record HeartbeatOperationTaskLeaseCommand(
    string OperationTaskId,
    HeartbeatOperationTaskLeaseRequest Request,
    DateTimeOffset Now) : ICommand<OperationTaskResponse>;

public sealed class HeartbeatOperationTaskLeaseCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<HeartbeatOperationTaskLeaseCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(HeartbeatOperationTaskLeaseCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.HeartbeatLeaseAsync(
            request.OperationTaskId,
            request.Request,
            request.Now,
            cancellationToken);
    }
}
