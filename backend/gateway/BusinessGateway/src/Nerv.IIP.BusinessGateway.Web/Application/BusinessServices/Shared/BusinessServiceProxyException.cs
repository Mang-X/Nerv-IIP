using System.Net;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class BusinessServiceProxyException : Exception
{
    public const string DownstreamRequestFailedMessage = "downstream-request-failed";

    private const int MaxErrorDataItems = 32;
    private const int MaxErrorDataDepth = 4;
    private const int MaxErrorDataValueLength = 4096;

    private static readonly string[] SensitivePropertyFragments =
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
        "csrf",
        "xsrf",
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
            IsStrictSafeDownstreamMessage(downstreamMessage)
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
            allowMessageAsSemanticCode && IsSemanticDownstreamCode(downstreamMessage);
        var envelopeCodeIsValid = semanticCodeIsValid || messageIsSemanticCode;
        var safeMessage = statusCode == HttpStatusCode.BadRequest
            ? IsSafeDownstreamBusinessMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage
            : IsStrictSafeDownstreamMessage(downstreamMessage)
                ? downstreamMessage!
                : DownstreamRequestFailedMessage;
        var semanticCode = semanticCodeIsValid
            ? downstreamCode!
            : messageIsSemanticCode
                ? downstreamMessage!
                : DownstreamRequestFailedMessage;

        return new(
            statusCode,
            envelopeCodeIsValid ? safeMessage : DownstreamRequestFailedMessage,
            semanticCode,
            envelopeCodeIsValid ? SanitizeErrorData(errorData) : [],
            innerException);
    }

    private static bool IsStrictSafeDownstreamMessage(string? downstreamMessage)
    {
        if (string.IsNullOrWhiteSpace(downstreamMessage) || downstreamMessage.Length > 128)
        {
            return false;
        }

        var first = downstreamMessage[0];
        if (!IsAsciiLetter(first) && !char.IsAsciiDigit(first))
        {
            return false;
        }

        return downstreamMessage.All(static value =>
            IsAsciiLetter(value) ||
            char.IsAsciiDigit(value) ||
            value is '-' or '_' or '.');
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

    private static bool IsSemanticDownstreamCode(string? downstreamCode) =>
        IsStrictSafeDownstreamMessage(downstreamCode) &&
        !downstreamCode!.All(static value => char.IsAsciiDigit(value));

    private static IReadOnlyCollection<JsonElement> SanitizeErrorData(
        IReadOnlyCollection<JsonElement>? errorData)
    {
        if (errorData is null || errorData.Count == 0)
        {
            return [];
        }

        var sanitized = new List<JsonElement>(Math.Min(errorData.Count, MaxErrorDataItems));
        foreach (var item in errorData.Take(MaxErrorDataItems))
        {
            if (TrySanitizeJsonElement(item, depth: 0, out var value))
            {
                sanitized.Add(value);
            }
        }

        return sanitized.ToArray();
    }

    private static bool TrySanitizeJsonElement(
        JsonElement value,
        int depth,
        out JsonElement sanitized)
    {
        sanitized = default;
        if (depth > MaxErrorDataDepth || value.GetRawText().Length > MaxErrorDataValueLength)
        {
            return false;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!IsStrictSafeDownstreamMessage(property.Name) ||
                        IsSensitiveProperty(property.Name) ||
                        !TrySanitizeJsonElement(property.Value, depth + 1, out var child))
                    {
                        continue;
                    }

                    properties[property.Name] = child;
                }

                sanitized = JsonSerializer.SerializeToElement(properties);
                return true;
            }
            case JsonValueKind.Array:
            {
                var items = new List<JsonElement>();
                foreach (var item in value.EnumerateArray().Take(MaxErrorDataItems))
                {
                    if (TrySanitizeJsonElement(item, depth + 1, out var child))
                    {
                        items.Add(child);
                    }
                }

                sanitized = JsonSerializer.SerializeToElement(items);
                return true;
            }
            case JsonValueKind.String:
            {
                var text = value.GetString();
                if (!IsStrictSafeDownstreamMessage(text))
                {
                    return false;
                }

                sanitized = JsonSerializer.SerializeToElement(text);
                return true;
            }
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            {
                using var document = JsonDocument.Parse(value.GetRawText());
                sanitized = document.RootElement.Clone();
                return true;
            }
            default:
                return false;
        }
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        var normalized = propertyName
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return SensitivePropertyFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.Ordinal));
    }
}
