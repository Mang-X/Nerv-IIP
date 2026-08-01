using System.Net;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Application.Errors;

public sealed class MesIdempotencyConflictException : Exception
{
    public const string SafeCode = "idempotency-conflict";
}

public sealed class MesLifecycleConflictException(string action, string currentStatus)
    : Exception($"MES lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed record MesLifecycleConflictResponse(bool Success, string Message);

public sealed class MesLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<MesLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (MesRoutingSnapshotMissingException exception)
        {
            logger.LogInformation(
                "MES routing snapshot missing. Source={Source}",
                exception.DiagnosticSource);
            context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(
                new MesLifecycleConflictResponse(false, MesRoutingSnapshotMissingException.SafeCode),
                context.RequestAborted);
        }
        catch (MesIdempotencyConflictException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new MesLifecycleConflictResponse(false, MesIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (MesLifecycleConflictException exception)
        {
            logger.LogInformation(
                "MES lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new MesLifecycleConflictResponse(false, MesLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }
}
