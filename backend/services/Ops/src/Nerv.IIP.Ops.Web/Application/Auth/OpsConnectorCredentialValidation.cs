using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.Ops.Web.Application.Auth;

public sealed class OpsConnectorCredentialOptions
{
    public const string SectionName = "ConnectorHostCredential";

    public string? Secret { get; init; }
    public DateTimeOffset? ValidFromUtc { get; init; }
    public DateTimeOffset? ValidToUtc { get; init; }
    public bool Revoked { get; init; }
}

public sealed class OpsIamClientOptions
{
    public const string SectionName = "Ops:IamClient";

    // Production defaults tolerate ordinary IAM latency and jitter; the budgets stay explicit and
    // separable so tests can override them to milliseconds through the Ops:IamClient section.
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed record OpsConnectorCredentialValidationRequest(
    string ConnectorHostId,
    string Secret,
    string OrganizationId,
    string EnvironmentId,
    string RequiredPermission);

public sealed record OpsConnectorCredentialValidationResult(
    bool IsAuthorized,
    string Reason,
    string? PrincipalType = null,
    string? OrganizationId = null,
    string? EnvironmentId = null,
    string? ConnectorHostId = null)
{
    public static OpsConnectorCredentialValidationResult Authorized(
        string principalType,
        string organizationId,
        string environmentId,
        string connectorHostId) =>
        new(true, "authorized", principalType, organizationId, environmentId, connectorHostId);

    public static OpsConnectorCredentialValidationResult Rejected(string reason) => new(false, reason);
}

public interface IOpsConnectorCredentialValidator
{
    Task<OpsConnectorCredentialValidationResult> ValidateAsync(
        OpsConnectorCredentialValidationRequest request,
        CancellationToken cancellationToken);
}

public sealed class OpsConnectorCredentialValidator(
    IWebHostEnvironment environment,
    IOptionsMonitor<OpsConnectorCredentialOptions> options,
    ConfiguredOpsConnectorCredentialValidator configuredValidator,
    IamOpsConnectorCredentialValidator iamValidator) : IOpsConnectorCredentialValidator
{
    public Task<OpsConnectorCredentialValidationResult> ValidateAsync(
        OpsConnectorCredentialValidationRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsProduction() && !string.IsNullOrWhiteSpace(options.CurrentValue.Secret))
        {
            return configuredValidator.ValidateAsync(request, cancellationToken);
        }

        return iamValidator.ValidateAsync(request, cancellationToken);
    }
}

public sealed class ConfiguredOpsConnectorCredentialValidator(IOptionsMonitor<OpsConnectorCredentialOptions> options)
{
    public Task<OpsConnectorCredentialValidationResult> ValidateAsync(
        OpsConnectorCredentialValidationRequest request,
        CancellationToken cancellationToken)
    {
        var credential = options.CurrentValue;
        var now = DateTimeOffset.UtcNow;
        if (credential.Revoked)
        {
            return Task.FromResult(OpsConnectorCredentialValidationResult.Rejected("credential-revoked"));
        }

        if (credential.ValidFromUtc is not null && credential.ValidFromUtc > now)
        {
            return Task.FromResult(OpsConnectorCredentialValidationResult.Rejected("credential-not-yet-valid"));
        }

        if (credential.ValidToUtc is not null && credential.ValidToUtc <= now)
        {
            return Task.FromResult(OpsConnectorCredentialValidationResult.Rejected("credential-expired"));
        }

        if (string.IsNullOrWhiteSpace(credential.Secret)
            || !FixedTimeEquals(credential.Secret, request.Secret))
        {
            return Task.FromResult(OpsConnectorCredentialValidationResult.Rejected("invalid-secret"));
        }

        return Task.FromResult(OpsConnectorCredentialValidationResult.Authorized(
            "connector-host",
            request.OrganizationId,
            request.EnvironmentId,
            request.ConnectorHostId));
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

public sealed class IamOpsConnectorCredentialValidator(HttpClient httpClient, ILogger<IamOpsConnectorCredentialValidator> logger)
{
    public async Task<OpsConnectorCredentialValidationResult> ValidateAsync(
        OpsConnectorCredentialValidationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/api/iam/v1/connectors/credentials/validate",
                new ValidateConnectorCredentialRequest(request.ConnectorHostId, request.Secret),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return OpsConnectorCredentialValidationResult.Rejected("iam-rejected");
            }

            if (!response.IsSuccessStatusCode)
            {
                LogFailure("business-response", (int)response.StatusCode);
                return OpsConnectorCredentialValidationResult.Rejected("iam-unavailable");
            }

            ConnectorPrincipalResponse? principal;
            try
            {
                principal = await response.Content.ReadFromJsonAsync<ConnectorPrincipalResponse>(cancellationToken);
            }
            catch (JsonException ex)
            {
                LogFailure("invalid-response", (int)response.StatusCode, ex);
                return OpsConnectorCredentialValidationResult.Rejected("iam-invalid-response");
            }
            catch (NotSupportedException ex)
            {
                LogFailure("invalid-response", (int)response.StatusCode, ex);
                return OpsConnectorCredentialValidationResult.Rejected("iam-invalid-response");
            }

            if (principal is null
                || string.IsNullOrWhiteSpace(principal.PrincipalType)
                || string.IsNullOrWhiteSpace(principal.OrganizationId)
                || string.IsNullOrWhiteSpace(principal.EnvironmentId)
                || string.IsNullOrWhiteSpace(principal.ConnectorHostId))
            {
                LogFailure("invalid-response", (int)response.StatusCode);
                return OpsConnectorCredentialValidationResult.Rejected("iam-invalid-response");
            }

            return OpsConnectorCredentialValidationResult.Authorized(
                principal.PrincipalType,
                principal.OrganizationId,
                principal.EnvironmentId,
                principal.ConnectorHostId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Fail closed: IAM unavailable means reject rather than allow. A future local credential
            // cache with bounded TTL can be considered if IAM downtime availability becomes a concern.
            LogFailure(ClassifyTransportFailure(ex), null, ex);
            return OpsConnectorCredentialValidationResult.Rejected("iam-unavailable");
        }
        catch (OperationCanceledException ex)
        {
            // Fail closed: IAM unavailable means reject rather than allow. A future local credential
            // cache with bounded TTL can be considered if IAM downtime availability becomes a concern.
            LogFailure("request-timeout", null, ex);
            return OpsConnectorCredentialValidationResult.Rejected("iam-unavailable");
        }
    }

    // The structured message and its properties stay free of credentials, request bodies and response
    // bodies; the originating exception is still passed through so production keeps its stack trace.
    private void LogFailure(string failureKind, int? statusCode, Exception? exception = null)
    {
        logger.LogWarning(
            exception,
            "ConnectorCredentialValidationIamFailure FailureKind={FailureKind} StatusCode={StatusCode}",
            failureKind,
            statusCode);
    }

    // Deliberate boundary duplication of Nerv.IIP.Testing's NetworkFailureClassifier: a shipped
    // assembly must not reference a test assembly. The two must stay semantically identical —
    // docs/architecture/backend-test-determinism.md ("网络结果与预算") requires either side to sync
    // the other and the table whenever the split changes.
    // OpsConnectorCredentialValidationTests.TransportFailures asserts both sides agree row by row.
    private static string ClassifyTransportFailure(HttpRequestException exception)
    {
        if (exception.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            return "dns";
        }

        // Not gated on HttpRequestError.ConnectionError: the socket error code is the authoritative
        // signal, and a resolver failure can surface under a different HttpRequestError bucket while
        // still carrying HostNotFound. Classifying off the bucket alone would report those as a
        // generic transport error and lose the DNS / refused / timeout split.
        if (FindSocketException(exception) is { } socketException)
        {
            switch (socketException.SocketErrorCode)
            {
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                case SocketError.NoRecovery:
                    return "dns";
                case SocketError.ConnectionRefused:
                    return "connection-refused";

                // "request-timeout" is the one timeout verdict on both sides and covers a
                // helper-owned timeout in *either* phase — the connect budget and the request budget
                // stay separately configured (ConnectTimeout / RequestTimeout above), but the
                // distinction the classification preserves is caller-owned vs helper-owned
                // cancellation, not connect-phase vs exchange-phase.
                case SocketError.TimedOut:
                    return "request-timeout";
                default:
                    break;
            }
        }

        return "transport-error";
    }

    // Starts at the exception itself, not at InnerException: the classifier must not depend on the
    // socket error being wrapped. Mirrors NetworkFailureClassifier.FindSocketException.
    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }
        }

        return null;
    }

    private sealed record ValidateConnectorCredentialRequest(string ConnectorHostId, string Secret);
    private sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
}
