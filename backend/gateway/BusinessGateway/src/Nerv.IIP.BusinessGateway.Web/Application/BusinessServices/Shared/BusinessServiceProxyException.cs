using System.Net;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class BusinessServiceProxyException : Exception
{
    public const string DownstreamRequestFailedMessage = "downstream-request-failed";

    private const int MaxErrorDataItems = 32;
    private const int MaxErrorDataValueLength = 4096;

    private static readonly HashSet<string> SensitiveSemanticCodeSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "cookie",
        "password",
        "secret",
        "token",
        "jwt",
        "credential",
        "session",
        "pin",
        "actor",
        "csrf",
        "xsrf",
    };

    private static readonly string[] SensitiveFieldValueFragments =
    [
        "authorization",
        "cookie",
        "password",
        "secret",
        "token",
        "jwt",
        "connectionstring",
        "accesskey",
        "refreshkey",
        "apikey",
        "privatekey",
        "clientkey",
        "credential",
        "sessionid",
        "actorid",
        "csrf",
        "xsrf",
        "pin",
    ];

    public BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : base(DownstreamRequestFailedMessage, innerException)
    {
        _ = message;
        StatusCode = statusCode;
        SemanticCode = null;
        ErrorData = [];
    }

    private BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string safeMessage,
        Exception? innerException,
        bool messageIsSafe)
        : base(messageIsSafe ? safeMessage : DownstreamRequestFailedMessage, innerException)
    {
        StatusCode = statusCode;
        SemanticCode = null;
        ErrorData = [];
    }

    private BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string safeMessage,
        string semanticCode,
        IReadOnlyCollection<JsonElement> errorData,
        Exception? innerException)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        SemanticCode = semanticCode;
        ErrorData = errorData;
    }

    public HttpStatusCode StatusCode { get; }

    internal string? SemanticCode { get; }

    internal IReadOnlyCollection<JsonElement> ErrorData { get; }

    public static BusinessServiceProxyException FromSafeDownstreamMessage(
        HttpStatusCode statusCode,
        string? downstreamMessage,
        Exception? innerException = null) =>
        new(
            statusCode,
            IsExplicitSafeProxyReason(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage,
            innerException,
            messageIsSafe: true);

    public static BusinessServiceProxyException FromDownstreamBusinessMessage(
        string? downstreamMessage,
        Exception? innerException = null) =>
        new(
            HttpStatusCode.BadRequest,
            IsSafeDownstreamBusinessMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage,
            innerException,
            messageIsSafe: true);

    internal static BusinessServiceProxyException FromDownstreamError(
        HttpStatusCode statusCode,
        string? downstreamCode,
        string? downstreamMessage,
        IReadOnlyCollection<JsonElement>? errorData,
        bool allowMessageAsSemanticCode,
        Exception? innerException = null)
    {
        var semanticCodeIsValid = IsSemanticDownstreamCode(downstreamCode);
        var messageIsSemanticCode =
            allowMessageAsSemanticCode && IsExplicitSafeProxyReason(downstreamMessage);
        var envelopeCodeIsValid = semanticCodeIsValid || messageIsSemanticCode;
        var semanticCode = semanticCodeIsValid
            ? downstreamCode!
            : messageIsSemanticCode
                ? downstreamMessage!
                : DownstreamRequestFailedMessage;
        var safeMessage = statusCode == HttpStatusCode.BadRequest
            ? IsSafeDownstreamBusinessMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage
            : envelopeCodeIsValid &&
                string.Equals(downstreamMessage, semanticCode, StringComparison.Ordinal)
                ? semanticCode
                : DownstreamRequestFailedMessage;

        return new(
            statusCode,
            envelopeCodeIsValid ? safeMessage : DownstreamRequestFailedMessage,
            semanticCode,
            envelopeCodeIsValid ? ProjectSafeErrorData(errorData) : [],
            innerException);
    }

    private static bool IsSafeDownstreamBusinessMessage(string? downstreamMessage)
    {
        if (string.IsNullOrWhiteSpace(downstreamMessage) || downstreamMessage.Length > 500)
        {
            return false;
        }

        var first = downstreamMessage[0];
        if (char.IsWhiteSpace(first))
        {
            return false;
        }

        return downstreamMessage.All(static value =>
            !char.IsControl(value) &&
            value is not '<' and not '>' and not '{' and not '}' and not '/' and not '\\');
    }

    private static bool IsAsciiLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsSemanticDownstreamCode(string? downstreamCode)
    {
        if (string.IsNullOrWhiteSpace(downstreamCode) ||
            downstreamCode.Length > 128 ||
            downstreamCode[0] is < 'a' or > 'z' ||
            !downstreamCode.Contains('-', StringComparison.Ordinal) ||
            downstreamCode.Contains("--", StringComparison.Ordinal) ||
            downstreamCode[^1] == '-' ||
            ContainsSensitiveSemanticCodeSegment(downstreamCode))
        {
            return false;
        }

        return downstreamCode.All(static value =>
            value is >= 'a' and <= 'z' ||
            char.IsAsciiDigit(value) ||
            value == '-');
    }

    private static bool IsExplicitSafeProxyReason(string? value) =>
        IsSemanticDownstreamCode(value) || IsLegacyUpperSnakeReasonCode(value);

    private static bool IsLegacyUpperSnakeReasonCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value[0] is < 'A' or > 'Z' ||
            !value.Contains('_', StringComparison.Ordinal) ||
            value.Contains("__", StringComparison.Ordinal) ||
            value[^1] == '_' ||
            ContainsSensitiveSemanticCodeSegment(value))
        {
            return false;
        }

        return value.All(static character =>
            character is >= 'A' and <= 'Z' ||
            char.IsAsciiDigit(character) ||
            character == '_');
    }

    private static bool ContainsSensitiveSemanticCodeSegment(string value) =>
        value.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Any(SensitiveSemanticCodeSegments.Contains);

    internal static IReadOnlyCollection<JsonElement> ProjectSafeErrorData(
        IReadOnlyCollection<JsonElement>? errorData)
    {
        if (errorData is null || errorData.Count == 0)
        {
            return [];
        }

        var sanitized = new List<JsonElement>(Math.Min(errorData.Count, MaxErrorDataItems));
        foreach (var item in errorData.Take(MaxErrorDataItems))
        {
            if (TryProjectContractErrorData(item, out var value))
            {
                sanitized.Add(value);
            }
        }

        return sanitized.ToArray();
    }

    private static bool TryProjectContractErrorData(
        JsonElement value,
        out JsonElement sanitized)
    {
        sanitized = default;
        if (value.ValueKind != JsonValueKind.Object ||
            value.GetRawText().Length > MaxErrorDataValueLength)
        {
            return false;
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.TryGetProperty("field", out var field) &&
            field.ValueKind == JsonValueKind.String &&
            IsContractSafeFieldValue(field.GetString()))
        {
            properties["field"] = field.GetString()!;
        }

        if (value.TryGetProperty("reason", out var reason) &&
            reason.ValueKind == JsonValueKind.String &&
            IsSemanticDownstreamCode(reason.GetString()))
        {
            properties["reason"] = reason.GetString()!;
        }

        if (properties.Count == 0)
        {
            return false;
        }

        sanitized = JsonSerializer.SerializeToElement(properties);
        return true;
    }

    private static bool IsContractSafeFieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !IsAsciiLetter(value[0]) ||
            !value.All(static character => IsAsciiLetter(character) || char.IsAsciiDigit(character)))
        {
            return false;
        }

        var normalized = value.ToLowerInvariant();
        return !SensitiveFieldValueFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.Ordinal));
    }
}
