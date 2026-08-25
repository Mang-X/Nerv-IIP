using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public sealed record ToolingOperationAuditContext
{
    private readonly string[] forbiddenCredentials;

    private ToolingOperationAuditContext(
        string actor,
        string correlationId,
        string causationId,
        string operationId,
        IReadOnlyCollection<string>? forbiddenCredentials)
    {
        this.forbiddenCredentials = forbiddenCredentials?
            .Where(credential => !string.IsNullOrWhiteSpace(credential))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        Actor = ValidateActor(actor, this.forbiddenCredentials);
        CorrelationId = ValidateIdentity(correlationId, "correlationId", this.forbiddenCredentials);
        CausationId = ValidateIdentity(causationId, "causationId", this.forbiddenCredentials);
        OperationId = ValidateIdentity(operationId, "operationId", this.forbiddenCredentials);
    }

    public string Actor { get; }
    public string CorrelationId { get; }
    public string CausationId { get; }
    public string OperationId { get; }

    internal static ToolingOperationAuditContext CreateFromTrustedBoundary(
        string actor,
        string correlationId,
        string causationId,
        string operationId,
        IReadOnlyCollection<string>? forbiddenCredentials = null) => new(
            actor,
            correlationId,
            causationId,
            operationId,
            forbiddenCredentials);

    internal string RequireAuditableText(string value, string fieldName)
    {
        var normalized = value?.Trim();
        if (!ToolingAuditIdentityPolicy.IsValidAuditText(normalized, forbiddenCredentials))
        {
            throw new KnownException($"工装写操作的 {fieldName} 不能包含凭据或敏感内容。");
        }

        return normalized!;
    }

    private static string ValidateActor(string value, IReadOnlyCollection<string> forbiddenCredentials)
    {
        if (!ToolingAuditIdentityPolicy.IsValidActor(value, forbiddenCredentials))
        {
            throw new KnownException("工装写操作需要合法的已授权用户主体标识。");
        }

        return value;
    }

    private static string ValidateIdentity(
        string value,
        string fieldName,
        IReadOnlyCollection<string> forbiddenCredentials)
    {
        if (!ToolingAuditIdentityPolicy.IsValidOpaqueIdentity(value, forbiddenCredentials))
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
            ReadRequiredHeader(httpContext.Request.Headers, "X-Idempotency-Key"),
            ReadAuthorizationCredentials(httpContext.Request.Headers));
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

    private static string[] ReadAuthorizationCredentials(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Authorization", out var values) || values.Count != 1 ||
            !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(values[0], out var authorization) ||
            !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return [];
        }

        return [authorization.Parameter];
    }
}
