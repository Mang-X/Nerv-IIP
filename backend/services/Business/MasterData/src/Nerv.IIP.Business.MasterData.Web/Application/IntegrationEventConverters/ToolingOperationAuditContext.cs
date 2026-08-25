using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public sealed record ToolingOperationAuditContext
{
    private ToolingOperationAuditContext(
        string actor,
        string correlationId,
        string causationId,
        string operationId)
    {
        Actor = actor;
        CorrelationId = correlationId;
        CausationId = causationId;
        OperationId = operationId;
    }

    public string Actor { get; }
    public string CorrelationId { get; }
    public string CausationId { get; }
    public string OperationId { get; }

    internal static ToolingOperationAuditContext CreateFromTrustedBoundary(
        string actor,
        string correlationId,
        string causationId,
        string operationId) => new(
            ValidateActor(actor),
            ValidateIdentity(correlationId, "correlationId"),
            ValidateIdentity(causationId, "causationId"),
            ValidateIdentity(operationId, "operationId"));

    private static string ValidateActor(string value)
    {
        if (!ToolingAuditIdentityPolicy.IsValidActor(value))
        {
            throw new KnownException("工装写操作需要合法的已授权用户主体标识。");
        }

        return value;
    }

    private static string ValidateIdentity(string value, string fieldName)
    {
        if (!ToolingAuditIdentityPolicy.IsValidOpaqueIdentity(value))
        {
            throw new KnownException($"工装写操作需要合法且不含敏感内容的 {fieldName}。");
        }

        return value;
    }
}

public interface IToolingOperationAuditContextAccessor
{
    ToolingOperationAuditContext GetRequiredContext();
}

public sealed class HttpToolingOperationAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IToolingOperationAuditContextAccessor
{
    public ToolingOperationAuditContext GetRequiredContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
        {
            throw new KnownException("工装写操作需要已认证的调用主体。");
        }

        var actor = string.Equals(
            httpContext.User.FindFirstValue("token_type"),
            "internal_service",
            StringComparison.Ordinal)
            ? ReadRequiredHeader(httpContext.Request.Headers, "X-Authenticated-Actor")
            : ResolveAuthenticatedUser(httpContext.User);
        return ToolingOperationAuditContext.CreateFromTrustedBoundary(
            actor,
            ReadRequiredHeader(httpContext.Request.Headers, "X-Correlation-Id"),
            ReadRequiredHeader(httpContext.Request.Headers, "X-Causation-Id"),
            ReadRequiredHeader(httpContext.Request.Headers, "X-Idempotency-Key"));
    }

    private static string ResolveAuthenticatedUser(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.Identity?.Name;
        return string.IsNullOrWhiteSpace(subject)
            ? throw new KnownException("工装写操作无法解析已认证用户主体。")
            : $"user:{subject}";
    }

    private static string ReadRequiredHeader(IHeaderDictionary headers, string name)
    {
        if (!headers.TryGetValue(name, out StringValues values) ||
            values.Count != 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            throw new KnownException($"工装写操作需要唯一且非空的 {name} 标头。");
        }

        return values[0]!;
    }
}
