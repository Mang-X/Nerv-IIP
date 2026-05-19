using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record AbandonOperationTaskLeaseCommand(
    string OperationTaskId,
    AbandonOperationTaskLeaseRequest Request,
    DateTimeOffset Now) : ICommand<OperationTaskResponse>;

public sealed class AbandonOperationTaskLeaseCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<AbandonOperationTaskLeaseCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(AbandonOperationTaskLeaseCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.AbandonLeaseAsync(
            request.OperationTaskId,
            request.Request,
            request.Now,
            cancellationToken);
    }
}
