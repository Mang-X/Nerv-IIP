using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Behaviors;

public interface IOperationTaskConcurrencyRetryCommand;

public sealed class ManualDispatchConcurrencyRetryBehavior<TRequest, TResponse>(ApplicationDbContext dbContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    private const int MaxAttempts = 3;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await next(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
                when (IsSupportedCommand(request) &&
                    attempt < MaxAttempts &&
                    IsManualDispatchRevisionConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsSupportedCommand(TRequest request) =>
        request is IOperationTaskConcurrencyRetryCommand;

    private static bool IsManualDispatchRevisionConflict(DbUpdateConcurrencyException exception)
    {
        return exception.Entries.Count > 0 && exception.Entries.All(entry =>
            entry.Entity is OperationTask &&
            entry.Metadata.FindProperty(nameof(OperationTask.ManualDispatchRevision))?.IsConcurrencyToken == true);
    }
}
