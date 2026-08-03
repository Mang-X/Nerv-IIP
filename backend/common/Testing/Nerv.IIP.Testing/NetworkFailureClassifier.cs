using System.Net;
using System.Net.Sockets;

namespace Nerv.IIP.Testing;

public enum NetworkFailureKind
{
    Dns,
    ConnectionRefused,
    RequestTimeout,
    BusinessError,
}

public sealed record NetworkFailureObservation(
    NetworkFailureKind Kind,
    HttpStatusCode? StatusCode,
    string Diagnostic);

public static class NetworkFailureClassifier
{
    public static NetworkFailureObservation FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is HttpRequestException
            {
                HttpRequestError: HttpRequestError.NameResolutionError,
            })
        {
            return new(NetworkFailureKind.Dns, null, "DNS name resolution failed.");
        }

        if (exception is HttpRequestException
            {
                HttpRequestError: HttpRequestError.ConnectionError,
            } && FindSocketException(exception) is { SocketErrorCode: SocketError.ConnectionRefused })
        {
            return new(NetworkFailureKind.ConnectionRefused, null, "Connection was refused.");
        }

        if (exception is OperationCanceledException)
        {
            return new(NetworkFailureKind.RequestTimeout, null, "Request timed out.");
        }

        throw new ArgumentException(
            "The exception is not a supported network failure.",
            nameof(exception));
    }

    public static NetworkFailureObservation FromResponse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode)
        {
            throw new ArgumentException(
                "A successful response is not a network failure.",
                nameof(response));
        }

        var reasonPhrase = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? string.Empty
            : $" {TestDiagnostic.Sanitize(response.ReasonPhrase)}";

        return new(
            NetworkFailureKind.BusinessError,
            response.StatusCode,
            $"HTTP {(int)response.StatusCode}{reasonPhrase}");
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }
        }

        return null;
    }
}
