using System.Net;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Errors;

public sealed class MaintenanceIdempotencyConflictException : Exception
{
    public const string SafeCode = "idempotency-conflict";
}

public sealed class MaintenanceLifecycleConflictException(string action, string currentStatus)
    : Exception($"Maintenance lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed record MaintenanceLifecycleConflictResponse(bool Success, string Message);

public sealed class MaintenanceLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<MaintenanceLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (MaintenanceIdempotencyConflictException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new MaintenanceLifecycleConflictResponse(false, MaintenanceIdempotencyConflictException.SafeCode),
                context.RequestAborted);
        }
        catch (MaintenanceLifecycleConflictException exception)
        {
            logger.LogInformation(
                "Maintenance lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new MaintenanceLifecycleConflictResponse(false, MaintenanceLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }
}
