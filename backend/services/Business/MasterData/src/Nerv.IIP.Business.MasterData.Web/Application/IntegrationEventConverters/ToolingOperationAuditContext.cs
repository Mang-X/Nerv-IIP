using System.Data.Common;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public interface IToolingOperationAdmission
{
    ToolingOperationAuditContext GetRequiredContext();
    ToolingOperationAuditContext.ToolingAuditSafeText RequireAuditSafeText(string value, string fieldName);
}

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

    public sealed record ToolingAuditSafeText
    {
        private ToolingAuditSafeText(string value) => Value = value;

        public string Value { get; }

        public sealed class HttpAdmission(IHttpContextAccessor httpContextAccessor)
            : IToolingOperationAdmission
        {
            private const int MaxIdentityLength = 200;
            private const int MaxAuditTextLength = 500;
            private const string UserActorPrefix = "user:";
            private static readonly HashSet<string> CredentialAssignmentKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "authorization",
            "password",
            "passwd",
            "pwd",
            "secret",
            "token",
        };
            private static readonly HashSet<string> ConnectionEndpointKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "host",
            "server",
            "data source",
            "address",
            "addr",
            "network address",
        };
            private static readonly HashSet<string> ConnectionContextKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "database",
            "initial catalog",
            "username",
            "user id",
            "uid",
            "port",
            "integrated security",
            "trusted_connection",
            "ssl mode",
        };
            private static readonly HashSet<string> PostgreSqlConnectionUriSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            "postgres",
            "postgresql",
        };

            public ToolingOperationAuditContext GetRequiredContext()
            {
                var httpContext = GetAuthenticatedHttpContext();
                var actor = string.Equals(
                    httpContext.User.FindFirstValue("token_type"),
                    "internal_service",
                    StringComparison.Ordinal)
                    ? ReadRequiredHeader(httpContext.Request.Headers, "X-Authenticated-Actor")
                    : ResolveAuthenticatedUser(httpContext.User);
                var forbiddenCredentials = ReadAuthorizationCredentials(httpContext.Request.Headers);
                return new ToolingOperationAuditContext(
                    RequireActor(actor, forbiddenCredentials),
                    RequireIdentity(
                        ReadRequiredHeader(httpContext.Request.Headers, "X-Correlation-Id"),
                        "correlationId",
                        forbiddenCredentials),
                    RequireIdentity(
                        ReadRequiredHeader(httpContext.Request.Headers, "X-Causation-Id"),
                        "causationId",
                        forbiddenCredentials),
                    RequireIdentity(
                        ReadRequiredHeader(httpContext.Request.Headers, "X-Idempotency-Key"),
                        "operationId",
                        forbiddenCredentials));
            }

            public ToolingAuditSafeText RequireAuditSafeText(string value, string fieldName)
            {
                var httpContext = GetAuthenticatedHttpContext();
                return new ToolingAuditSafeText(RequireAuditText(
                    value,
                    fieldName,
                    ReadAuthorizationCredentials(httpContext.Request.Headers)));
            }

            private HttpContext GetAuthenticatedHttpContext()
            {
                var httpContext = httpContextAccessor.HttpContext;
                return httpContext?.User.Identity?.IsAuthenticated == true
                    ? httpContext
                    : throw new KnownException("工装写操作需要已认证的调用主体。");
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
                    !AuthenticationHeaderValue.TryParse(values[0], out var authorization) ||
                    !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(authorization.Parameter))
                {
                    return [];
                }

                return [authorization.Parameter];
            }

            private static string RequireActor(string value, IReadOnlyCollection<string> forbiddenCredentials)
            {
                if (value is null ||
                    value.Length <= UserActorPrefix.Length ||
                    value.Length > MaxIdentityLength ||
                    !value.StartsWith(UserActorPrefix, StringComparison.Ordinal) ||
                    !IsCanonicalToken(value.AsSpan(UserActorPrefix.Length)) ||
                    ContainsSensitiveContent(value, forbiddenCredentials))
                {
                    throw new KnownException("工装写操作需要合法的已授权用户主体标识。");
                }

                return value;
            }

            private static string RequireIdentity(
                string value,
                string fieldName,
                IReadOnlyCollection<string> forbiddenCredentials)
            {
                if (value is null ||
                    value.Length is <= 0 or > MaxIdentityLength ||
                    !IsCanonicalToken(value.AsSpan()) ||
                    ContainsSensitiveContent(value, forbiddenCredentials))
                {
                    throw new KnownException($"工装写操作需要合法且不含敏感内容的 {fieldName}。");
                }

                return value;
            }

            private static string RequireAuditText(
                string value,
                string fieldName,
                IReadOnlyCollection<string> forbiddenCredentials)
            {
                var normalized = value?.Trim();
                if (string.IsNullOrWhiteSpace(normalized) ||
                    normalized.Length > MaxAuditTextLength ||
                    normalized.Any(char.IsControl) ||
                    ContainsSensitiveContent(normalized, forbiddenCredentials))
                {
                    throw new KnownException($"工装写操作的 {fieldName} 不能包含凭据或敏感内容。");
                }

                return normalized;
            }

            private static bool IsCanonicalToken(ReadOnlySpan<char> value)
            {
                if (value.IsEmpty || !IsAsciiAlphaNumeric(value[0])) return false;
                foreach (var character in value)
                {
                    if (IsAsciiAlphaNumeric(character) || character is '-' or '_' or '.' or '/') continue;
                    return false;
                }

                return true;
            }

            private static bool IsAsciiAlphaNumeric(char value) =>
                value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

            private static bool ContainsSensitiveContent(
                string value,
                IReadOnlyCollection<string> forbiddenCredentials) =>
                ContainsBearerCredential(value) ||
                ContainsCredentialAssignment(value) ||
                ContainsCompactJwt(value) ||
                ContainsConnectionString(value) ||
                forbiddenCredentials.Any(credential =>
                    !string.IsNullOrEmpty(credential) &&
                    (string.Equals(value, credential, StringComparison.Ordinal) ||
                        string.Equals(value, $"{UserActorPrefix}{credential}", StringComparison.Ordinal)));

            private static bool ContainsBearerCredential(string value) =>
                AuthenticationHeaderValue.TryParse(value.Trim(), out var authorization) &&
                string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(authorization.Parameter);

            private static bool ContainsCredentialAssignment(string value)
            {
                foreach (var segment in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = segment.IndexOfAny(['=', ':']);
                    if (separator <= 0 || separator == segment.Length - 1) continue;
                    if (CredentialAssignmentKeys.Contains(segment[..separator].Trim())) return true;
                }

                return false;
            }

            private static bool ContainsConnectionString(string value)
            {
                if (ContainsPostgreSqlConnectionUri(value)) return true;
                if (!value.Contains(';') || !value.Contains('=')) return false;

                try
                {
                    var builder = new DbConnectionStringBuilder { ConnectionString = value };
                    var keys = builder.Keys.Cast<string>().ToArray();
                    return keys.Any(ConnectionEndpointKeys.Contains) &&
                        keys.Any(ConnectionContextKeys.Contains);
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            private static bool ContainsPostgreSqlConnectionUri(string value) =>
                Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
                PostgreSqlConnectionUriSchemes.Contains(uri.Scheme) &&
                !string.IsNullOrWhiteSpace(uri.Host);

            private static bool ContainsCompactJwt(string value)
            {
                char[] separators = [' ', '\t', '\r', '\n', ':', '=', '"', '\'', ',', ';', '(', ')', '[', ']', '<', '>'];
                return value.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .Any(IsCompactJwt);
            }

            private static bool IsCompactJwt(string candidate)
            {
                var segments = candidate.Split('.');
                if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty)) return false;
                try
                {
                    var headerBytes = Convert.FromBase64String(ToBase64(segments[0]));
                    using var header = JsonDocument.Parse(headerBytes);
                    return header.RootElement.ValueKind == JsonValueKind.Object &&
                        header.RootElement.TryGetProperty("alg", out _);
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            private static string ToBase64(string value)
            {
                var normalized = value.Replace('-', '+').Replace('_', '/');
                return normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            }
        }
    }
}
