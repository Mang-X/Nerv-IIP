using System.Net;
using System.Net.Sockets;

namespace Nerv.IIP.Testing.Tests;

public sealed class NetworkFailureClassifierTests
{
    [Fact]
    public void FromException_ClassifiesNameResolutionFailure()
    {
        var exception = new HttpRequestException(
            HttpRequestError.NameResolutionError,
            "Name or service not known");

        var observation = NetworkFailureClassifier.FromException(exception);

        Assert.Equal(NetworkFailureKind.Dns, observation.Kind);
        Assert.Null(observation.StatusCode);
        Assert.Equal("DNS name resolution failed.", observation.Diagnostic);
    }

    [Fact]
    public void FromException_ClassifiesConnectionRefused()
    {
        var exception = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "Connection failed",
            new SocketException((int)SocketError.ConnectionRefused));

        var observation = NetworkFailureClassifier.FromException(exception);

        Assert.Equal(NetworkFailureKind.ConnectionRefused, observation.Kind);
        Assert.Null(observation.StatusCode);
        Assert.Equal("Connection was refused.", observation.Diagnostic);
    }

    [Theory]
    [MemberData(nameof(TimeoutExceptions))]
    public void FromException_ClassifiesHelperOwnedCancellationAsRequestTimeout(Exception exception)
    {
        var observation = NetworkFailureClassifier.FromException(exception);

        Assert.Equal(NetworkFailureKind.RequestTimeout, observation.Kind);
        Assert.Null(observation.StatusCode);
        Assert.Equal("Request timed out.", observation.Diagnostic);
    }

    [Fact]
    public void FromResponse_ClassifiesNonSuccessWithoutResponseBodyOrHeaders()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            ReasonPhrase = "Business Rule Rejected",
            Content = new StringContent("password=raw-response-secret"),
        };
        response.Headers.TryAddWithoutValidation("X-Credential", "raw-header-secret");

        var observation = NetworkFailureClassifier.FromResponse(response);

        Assert.Equal(NetworkFailureKind.BusinessError, observation.Kind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, observation.StatusCode);
        Assert.Equal("HTTP 422 Business Rule Rejected", observation.Diagnostic);
        Assert.DoesNotContain("raw-response-secret", observation.Diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-header-secret", observation.Diagnostic, StringComparison.Ordinal);
    }

    public static TheoryData<Exception> TimeoutExceptions => new()
    {
        new TaskCanceledException("helper timeout"),
        new OperationCanceledException("helper timeout"),
    };
}
