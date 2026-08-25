using System.Net;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class BusinessServiceProxyException : Exception
{
    public const string DownstreamRequestFailedMessage = "downstream-request-failed";

    public BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : base(DownstreamRequestFailedMessage, innerException)
    {
        _ = message;
        StatusCode = statusCode;
    }

    private BusinessServiceProxyException(
        HttpStatusCode statusCode,
        string safeMessage,
        Exception? innerException,
        bool messageIsSafe)
        : base(messageIsSafe ? safeMessage : DownstreamRequestFailedMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

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
}
