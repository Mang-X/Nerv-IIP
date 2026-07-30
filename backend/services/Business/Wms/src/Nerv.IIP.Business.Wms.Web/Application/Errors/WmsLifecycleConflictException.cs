using System.Net;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;

namespace Nerv.IIP.Business.Wms.Web.Application.Errors;

public sealed class WmsIdempotencyConflictException : Exception
{
    public const string SafeCode = "idempotency-conflict";
}

public sealed class WmsAuthorizationException : Exception
{
    public const string SafeCode = "forbidden";

    private WmsAuthorizationException(string reason)
        : base($"WMS authorization denied: {reason}.")
    {
        Reason = reason;
    }

    public string Reason { get; }

    public static WmsAuthorizationException Forbidden(string reason) =>
        new(reason);
}

public sealed class WmsUnprocessableException(string reason)
    : Exception($"WMS request cannot be processed: {reason}.")
{
    public const string SafeCode = "unprocessable";

    public string Reason { get; } = reason;
}

public sealed class WmsLifecycleConflictException(string action, string currentStatus)
    : Exception($"WMS lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed record WmsLifecycleConflictResponse(bool Success, string Message);

public sealed class WmsLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<WmsLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        try
        {
            await next(context);
        }
        catch (WmsIdempotencyConflictException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (DbUpdateException exception) when (
            WmsIdempotencyPersistenceConflicts.IsTargetConflict(exception, dbContext))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (DbUpdateException exception) when (
            WmsWcsDispatchPersistenceConflicts.IsTargetConflict(exception, dbContext))
        {
            logger.LogInformation(
                "WMS WCS dispatch persistence conflict on the warehouse-task ownership constraint.");
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (WmsAuthorizationException exception)
        {
            logger.LogInformation(
                "WMS authorization denied. Reason={Reason}",
                exception.Reason);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsAuthorizationException.SafeCode),
                context.RequestAborted);
        }
        catch (WmsUnprocessableException exception)
        {
            logger.LogInformation(
                "WMS request is unprocessable. Reason={Reason}",
                exception.Reason);
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsUnprocessableException.SafeCode),
                context.RequestAborted);
        }
        catch (WmsLifecycleConflictException exception)
        {
            logger.LogInformation(
                "WMS lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, WmsLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }
}

public static class WmsIdempotencyPersistenceConflicts
{
    public static bool IsTargetConflict(DbUpdateException exception, ApplicationDbContext dbContext)
    {
        var expectedConstraint = dbContext.Model.FindEntityType(typeof(InventoryMovementRequest))
            ?.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(InventoryMovementRequest.OrganizationId),
                    nameof(InventoryMovementRequest.EnvironmentId),
                    nameof(InventoryMovementRequest.SourceDocumentId),
                    nameof(InventoryMovementRequest.IdempotencyKey),
                ]))
            .GetDatabaseName();
        return MatchesPostgreSqlUniqueConstraint(exception, expectedConstraint);
    }

    internal static bool MatchesPostgreSqlUniqueConstraint(
        DbUpdateException exception,
        string? expectedConstraint)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            var constraintName = current.GetType().GetProperty("ConstraintName")?.GetValue(current) as string;
            if (string.Equals(sqlState, "23505", StringComparison.Ordinal)
                && string.Equals(constraintName, expectedConstraint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public static class WmsWcsDispatchPersistenceConflicts
{
    public static bool IsTargetConflict(
        DbUpdateException exception,
        ApplicationDbContext dbContext)
    {
        var expectedConstraint = dbContext.Model.FindEntityType(typeof(WcsTask))
            ?.GetIndexes()
            .Single(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(WcsTask.WarehouseTaskId)]))
            .GetDatabaseName();
        return WmsIdempotencyPersistenceConflicts.MatchesPostgreSqlUniqueConstraint(
            exception,
            expectedConstraint);
    }
}

public static class WarehouseTaskActionReceiptPersistenceConflicts
{
    public static bool IsTargetConflict(
        DbUpdateException exception,
        ApplicationDbContext dbContext)
    {
        var expectedConstraint = dbContext.Model
            .FindEntityType(typeof(WarehouseTaskActionReceipt))
            ?.GetIndexes()
            .Single(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [
                        nameof(WarehouseTaskActionReceipt.OrganizationId),
                        nameof(WarehouseTaskActionReceipt.EnvironmentId),
                        nameof(WarehouseTaskActionReceipt.WarehouseTaskId),
                        nameof(WarehouseTaskActionReceipt.Action),
                        nameof(WarehouseTaskActionReceipt.IdempotencyKey),
                    ]))
            .GetDatabaseName();
        return WmsIdempotencyPersistenceConflicts.MatchesPostgreSqlUniqueConstraint(
            exception,
            expectedConstraint);
    }
}
