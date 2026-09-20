using System.Net;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Errors;

/// <summary>
/// Maintenance 命令层用 <c>KnownException</c> 直接外发的稳定 wire 码注册表（#3155）。
///
/// 这三条码的异常消息**就是码本身**、不含任何中文，经网关原样进 <c>message</c> 位，
/// 前端 <c>STABLE_ERROR_MESSAGES</c> 认不出就裸码上屏。收进本类是为了让它们落在
/// <c>scripts/verify-stable-code-frontend-vocabulary.ps1</c> 的扫描面内：
/// 检查器按 <c>*StableWireCodes</c> 类名后缀在 <c>backend/**/src/**</c> 全树发现注册表。
/// </summary>
public static class MaintenanceStableWireCodes
{
    public const string StoredWorkOrderReceiptIsInvalid = "stored-maintenance-work-order-receipt-is-invalid";
    public const string SourceAlarmAlreadyBound = "source-alarm-already-bound-to-a-different-create-intent";
    public const string StoredCompletionReceiptIsInvalid = "stored-maintenance-completion-receipt-is-invalid";
}

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
