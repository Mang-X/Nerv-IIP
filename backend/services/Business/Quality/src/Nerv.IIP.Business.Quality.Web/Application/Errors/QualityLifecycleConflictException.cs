using System.Net;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Quality.Web.Application.Errors;

public sealed class QualityIdempotencyConflictException : Exception
{
    public const string SafeCode = "idempotency-conflict";
}

public sealed class QualityAuthorizationException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;

    public static QualityAuthorizationException Forbidden(string reason) => new(reason);
}

public sealed class QualityUnprocessableException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

public sealed class QualityLifecycleConflictException(string action, string currentStatus)
    : Exception($"Quality lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed record QualityLifecycleConflictResponse(bool Success, string Message);

public sealed class QualityLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<QualityLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        try
        {
            await next(context);
        }
        catch (QualityIdempotencyConflictException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new QualityLifecycleConflictResponse(false, QualityIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (QualityAuthorizationException exception)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new QualityLifecycleConflictResponse(false, exception.Reason),
                context.RequestAborted);
        }
        catch (QualityUnprocessableException exception)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(
                new QualityLifecycleConflictResponse(false, exception.Reason),
                context.RequestAborted);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(
                exception,
                "Quality persistence concurrency conflict.");
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new QualityLifecycleConflictResponse(false, QualityLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (DbUpdateException exception) when (
            QualityIdempotencyPersistenceConflicts.IsTargetConflict(exception, dbContext))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new QualityLifecycleConflictResponse(false, QualityIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (QualityLifecycleConflictException exception)
        {
            logger.LogInformation(
                "Quality lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new QualityLifecycleConflictResponse(false, QualityLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }
}

public static class QualityIdempotencyPersistenceConflicts
{
    public static bool IsTargetConflict(DbUpdateException exception, ApplicationDbContext dbContext)
    {
        var expectedConstraints = new[]
        {
            FindConstraint(
                dbContext,
                typeof(CodeIdempotencyKey),
                [
                    nameof(CodeIdempotencyKey.OrganizationId),
                    nameof(CodeIdempotencyKey.EnvironmentId),
                    nameof(CodeIdempotencyKey.RuleKey),
                    nameof(CodeIdempotencyKey.IdempotencyKey),
                ]),
            FindConstraint(
                dbContext,
                typeof(InspectionTaskAssignmentReceipt),
                [
                    nameof(InspectionTaskAssignmentReceipt.OrganizationId),
                    nameof(InspectionTaskAssignmentReceipt.EnvironmentId),
                    nameof(InspectionTaskAssignmentReceipt.InspectionTaskId),
                    nameof(InspectionTaskAssignmentReceipt.Action),
                    nameof(InspectionTaskAssignmentReceipt.IdempotencyKey),
                ]),
        };
        return expectedConstraints.Any(expectedConstraint =>
            MatchesPostgreSqlUniqueConstraint(exception, expectedConstraint));
    }

    private static string? FindConstraint(
        ApplicationDbContext dbContext,
        Type entityType,
        IReadOnlyCollection<string> propertyNames) =>
        dbContext.Model.FindEntityType(entityType)
            ?.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(propertyNames))
            .GetDatabaseName();

    private static bool MatchesPostgreSqlUniqueConstraint(
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
