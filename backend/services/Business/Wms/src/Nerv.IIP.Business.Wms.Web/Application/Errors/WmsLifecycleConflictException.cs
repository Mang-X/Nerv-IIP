using System.Net;

namespace Nerv.IIP.Business.Wms.Web.Application.Errors;

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
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
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
