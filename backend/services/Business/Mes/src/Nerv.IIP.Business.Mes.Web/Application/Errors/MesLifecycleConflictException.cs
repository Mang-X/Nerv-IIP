using System.Net;

namespace Nerv.IIP.Business.Mes.Web.Application.Errors;

public sealed class MesLifecycleConflictException(string action, string currentStatus)
    : Exception($"MES lifecycle conflict for action '{action}' at status '{currentStatus}'.")
{
    public const string SafeCode = "lifecycle-conflict";

    public string Action { get; } = action;

    public string CurrentStatus { get; } = currentStatus;
}

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
        catch (MesLifecycleConflictException exception)
        {
            logger.LogInformation(
                "MES lifecycle conflict. Action={Action}, CurrentStatus={CurrentStatus}",
                exception.Action,
                exception.CurrentStatus);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(
                new LifecycleConflictResponse(false, MesLifecycleConflictException.SafeCode),
                context.RequestAborted);
        }
    }

    private sealed record LifecycleConflictResponse(bool Success, string Message);
}
