using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Commands;

public sealed record RecordOperationResultCommand(OperationResult Result) : ICommand<OperationTaskResponse>;

public sealed class RecordOperationResultCommandHandler(IOperationTaskApplicationService operationTasks)
    : ICommandHandler<RecordOperationResultCommand, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(RecordOperationResultCommand request, CancellationToken cancellationToken)
    {
        return await operationTasks.RecordResultAsync(request.Result, cancellationToken);
    }
}
