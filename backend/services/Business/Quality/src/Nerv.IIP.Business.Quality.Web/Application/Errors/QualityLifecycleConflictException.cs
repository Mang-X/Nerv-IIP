using System.Net;

namespace Nerv.IIP.Business.Quality.Web.Application.Errors;

public sealed class QualityLifecycleConflictException(string action, string currentStatus)
    : Exception($"Quality lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

public sealed class QualityLifecycleConflictMiddleware(
    RequestDelegate next,
    ILogger<QualityLifecycleConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (QualityLifecycleConflictException exception)
        {
            logger.LogInformation(
                "Quality lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new LifecycleConflictResponse(false, QualityLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }

    private sealed record LifecycleConflictResponse(bool Success, string Message);
}
