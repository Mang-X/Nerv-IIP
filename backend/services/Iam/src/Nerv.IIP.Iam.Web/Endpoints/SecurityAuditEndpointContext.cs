using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;

namespace Nerv.IIP.Iam.Web.Endpoints;

internal static class IamSecurityAuditEndpointContext
{
    public static SecurityAuditContext Create(HttpContext context, CurrentPrincipalResponse? principal)
    {
        return new SecurityAuditContext(
            principal is null ? "unknown" : $"{principal.PrincipalType}:{principal.UserId}",
            CorrelationId(context),
            context.Connection.RemoteIpAddress?.ToString(),
            principal?.OrganizationId ?? "unknown",
            principal?.EnvironmentId ?? "unknown");
    }

    private static string CorrelationId(HttpContext context)
    {
        return context.Request.Headers.TryGetValue("X-Correlation-Id", out var header) && !string.IsNullOrWhiteSpace(header)
            ? header.ToString()
            : context.TraceIdentifier;
    }
}
