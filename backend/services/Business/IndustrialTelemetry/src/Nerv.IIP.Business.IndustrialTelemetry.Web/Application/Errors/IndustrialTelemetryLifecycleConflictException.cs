using System.Net;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmShelveIdempotencyAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Errors;

public sealed class IndustrialTelemetryIdempotencyConflictException : Exception
{
    public const string SafeCode = "idempotency-conflict";
}

public sealed class IndustrialTelemetryLifecycleConflictException(string action, string currentStatus)
    : Exception($"Industrial Telemetry lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed record IndustrialTelemetryLifecycleConflictResponse(bool Success, string Message);

public sealed class IndustrialTelemetryLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<IndustrialTelemetryLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        try
        {
            await next(context);
        }
        catch (IndustrialTelemetryIdempotencyConflictException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new IndustrialTelemetryLifecycleConflictResponse(
                    false,
                    IndustrialTelemetryIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (DbUpdateException exception) when (
            IndustrialTelemetryIdempotencyPersistenceConflicts.IsTargetConflict(exception, dbContext))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new IndustrialTelemetryLifecycleConflictResponse(
                    false,
                    IndustrialTelemetryIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (IndustrialTelemetryLifecycleConflictException exception)
        {
            logger.LogInformation(
                "Industrial Telemetry lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new IndustrialTelemetryLifecycleConflictResponse(
                    false,
                    IndustrialTelemetryLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }
}

public static class IndustrialTelemetryIdempotencyPersistenceConflicts
{
    public static bool IsTargetConflict(DbUpdateException exception, ApplicationDbContext dbContext)
    {
        var expectedConstraint = dbContext.Model.FindEntityType(typeof(AlarmShelveIdempotency))
            ?.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(AlarmShelveIdempotency.OrganizationId),
                    nameof(AlarmShelveIdempotency.EnvironmentId),
                    nameof(AlarmShelveIdempotency.AlarmEventId),
                    nameof(AlarmShelveIdempotency.IdempotencyKey),
                ]))
            .GetDatabaseName();
        return MatchesPostgreSqlUniqueConstraint(exception, expectedConstraint);
    }

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
