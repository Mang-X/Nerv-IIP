using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Web.Application.Commands;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Queries;

public sealed record GetOperationTaskQuery(string OperationTaskId) : IQuery<OperationTaskResponse>;

public sealed class GetOperationTaskQueryHandler(IOperationTaskApplicationService operationTasks)
    : IQueryHandler<GetOperationTaskQuery, OperationTaskResponse>
{
    public async Task<OperationTaskResponse> Handle(GetOperationTaskQuery request, CancellationToken cancellationToken)
    {
        return await operationTasks.GetAsync(request.OperationTaskId, cancellationToken);
    }
}
