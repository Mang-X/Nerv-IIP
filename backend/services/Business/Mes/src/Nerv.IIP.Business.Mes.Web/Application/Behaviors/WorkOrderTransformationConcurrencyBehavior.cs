using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Behaviors;

public interface IWorkOrderTransformationConcurrencyCommand;

/// <summary>
/// Converts races on the PR-A work-order version token and transformation idempotency
/// unique key into deterministic application behavior. A unique-key race is retried so
/// the losing request can observe and replay the committed transformation; a stale source
/// version is retried once the same way and becomes a 409 when the state remains contested.
/// </summary>
public sealed class WorkOrderTransformationConcurrencyBehavior<TRequest, TResponse>(
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
                when (IsSupportedCommand(request) && IsWorkOrderRevisionConflict(exception) && attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException exception)
                when (IsSupportedCommand(request) && IsWorkOrderRevisionConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                throw new MesLifecycleConflictException(
                    "work-order-transformation",
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
