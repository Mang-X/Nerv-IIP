using System.Net;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Errors;

public sealed class IndustrialTelemetryLifecycleConflictException(string action, string currentStatus)
    : Exception($"Industrial Telemetry lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed class IndustrialTelemetryLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<IndustrialTelemetryLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (IndustrialTelemetryLifecycleConflictException exception)
        {
            logger.LogInformation(
                "Industrial Telemetry lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new LifecycleConflictResponse(false, IndustrialTelemetryLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }

    private sealed record LifecycleConflictResponse(bool Success, string Message);
}
