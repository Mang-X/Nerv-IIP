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

public sealed class WmsUnprocessableException(string reason, string? reasonCode = null)
    : Exception($"WMS request cannot be processed: {reason}.")
{
    public const string SafeCode = "unprocessable";

    public string Reason { get; } = reason;

    /// <summary>
    /// 稳定的机读拒绝原因（kebab ASCII）。
    ///
    /// 为什么不是直接把 <see cref="Reason"/> 上屏：网关的 <c>IsStrictSafeDownstreamMessage</c>
    /// 只放行 <c>[A-Za-z0-9-_.]</c>、≤128 字符的下游消息——这是防止下游自由文本（含 SQL 片段、
    /// 内部标识）经网关泄漏到浏览器的护栏，不该为了显示中文而拆掉。
    /// 因此本服务对外只承诺**稳定代码**，中文人话由前端按代码映射
    /// （与 <c>downstream-timeout</c> 的既有做法同款，见 business-console 的 notify.ts）。
    ///
    /// 没有给代码时退回 <see cref="SafeCode"/>，行为与改造前一致。
    /// </summary>
    public string ReasonCode { get; } =
        string.IsNullOrWhiteSpace(reasonCode) ? SafeCode : reasonCode;
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
            // Reason 本身就是稳定 kebab 代码（resource-not-assigned-to-self 等），可安全外发；
            // 只报 "forbidden" 会让「这单派给别人了」和「不在你的作业范围」看起来一模一样（#1397 / 台账 #82）。
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, SafeOutboundCode(exception.Reason, WmsAuthorizationException.SafeCode)),
                context.RequestAborted);
        }
        catch (WmsUnprocessableException exception)
        {
            logger.LogInformation(
                "WMS request is unprocessable. ReasonCode={ReasonCode}, Reason={Reason}",
                exception.ReasonCode,
                exception.Reason);
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(
                new WmsLifecycleConflictResponse(false, SafeOutboundCode(exception.ReasonCode, WmsUnprocessableException.SafeCode)),
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

    /// <summary>
    /// 只放行稳定的 kebab 代码外发，其余退回常量 SafeCode。
    ///
    /// 这里刻意复刻 BusinessGateway 的 <c>IsStrictSafeDownstreamMessage</c> 判据
    /// （首字符字母/数字，整体仅 <c>[A-Za-z0-9-_.]</c>，≤128）：网关本来就会把不合规的下游消息
    /// 换成 <c>downstream-request-failed</c>，本服务先自查一遍，避免出现「本地看着有原因、
    /// 过了网关变成一句 downstream-request-failed」这种更难排查的情况。
    /// </summary>
    public static string SafeOutboundCode(string? code, string fallback)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128)
        {
            return fallback;
        }

        if (!char.IsAsciiLetterOrDigit(code[0]))
        {
            return fallback;
        }

        return code.All(static value =>
            char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.')
            ? code
            : fallback;
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
