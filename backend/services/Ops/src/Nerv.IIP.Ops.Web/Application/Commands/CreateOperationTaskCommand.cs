using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record CreateOperationTaskCommand(CreateOperationTaskRequest Request, DateTimeOffset Now) : ICommand<OperationTaskResponse>;

public sealed class CreateOperationTaskCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<CreateOperationTaskCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(CreateOperationTaskCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.CreateAsync(request.Request, request.Now, cancellationToken);
    }
}
