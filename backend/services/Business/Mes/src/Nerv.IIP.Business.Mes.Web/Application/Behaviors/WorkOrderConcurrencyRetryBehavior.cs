using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Behaviors;

public interface IWorkOrderConcurrencyRetryCommand;

public interface IWorkOrderTransformationConcurrencyCommand : IWorkOrderConcurrencyRetryCommand;

/// <summary>
/// Retries declared work-order writes from fresh tracked state after a version conflict.
/// Transformation commands additionally recover their existing idempotency unique-key races.
/// Each attempt reruns the complete unit of work; persistent contention becomes a safe 409.
/// </summary>
public sealed class WorkOrderConcurrencyRetryBehavior<TRequest, TResponse>(
    ApplicationDbContext dbContext)
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
                when (request is IWorkOrderConcurrencyRetryCommand && IsWorkOrderRevisionConflict(exception) && attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException exception)
                when (request is IWorkOrderConcurrencyRetryCommand && IsWorkOrderRevisionConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                throw new MesLifecycleConflictException(
                    request is IWorkOrderTransformationConcurrencyCommand ? "work-order-transformation" : "work-order-write",
                    "concurrent-update");
            }
            catch (DbUpdateException exception)
                when (IsSupportedCommand(request) && IsUniqueConstraintConflict(exception) && attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception)
                when (IsSupportedCommand(request) && IsUniqueConstraintConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                throw new MesLifecycleConflictException(
                    "work-order-transformation",
                    "concurrent-idempotency-write");
            }
        }
    }

    private static bool IsSupportedCommand(TRequest request) =>
        request is IWorkOrderTransformationConcurrencyCommand;

    private static bool IsWorkOrderRevisionConflict(DbUpdateConcurrencyException exception) =>
        exception.Entries.Count > 0 && exception.Entries.All(entry =>
            entry.Entity is WorkOrder &&
            entry.Metadata.FindProperty(nameof(WorkOrder.Version))?.IsConcurrencyToken == true);

    private static bool IsUniqueConstraintConflict(DbUpdateException exception)
    {
        var providerException = exception.InnerException;
        if (providerException is null)
        {
            return false;
        }

        var exceptionType = providerException.GetType();
        var sqlState = exceptionType.GetProperty("SqlState")?.GetValue(providerException) as string;
        if (string.Equals(sqlState, "23505", StringComparison.Ordinal))
        {
            return true;
        }

        var sqliteErrorCode = exceptionType.GetProperty("SqliteErrorCode")?.GetValue(providerException);
        return sqliteErrorCode is int errorCode && errorCode == 19;
    }
}
