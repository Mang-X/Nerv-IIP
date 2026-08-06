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

        var observation = NetworkFailureClassifier.FromException(exception, CancellationToken.None);

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

        var observation = NetworkFailureClassifier.FromException(exception, CancellationToken.None);

        Assert.Equal(NetworkFailureKind.ConnectionRefused, observation.Kind);
        Assert.Null(observation.StatusCode);
        Assert.Equal("Connection was refused.", observation.Diagnostic);
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, NetworkFailureKind.ConnectionRefused, "Connection was refused.")]
    [InlineData(SocketError.HostNotFound, NetworkFailureKind.Dns, "DNS name resolution failed.")]
    [InlineData(SocketError.NoData, NetworkFailureKind.Dns, "DNS name resolution failed.")]
    [InlineData(SocketError.TryAgain, NetworkFailureKind.Dns, "DNS name resolution failed.")]
    [InlineData(SocketError.TimedOut, NetworkFailureKind.RequestTimeout, "Request timed out.")]
    public void FromException_ClassifiesNonHttpSocketFailuresWithTheSameVocabulary(
        SocketError socketError,
        NetworkFailureKind expectedKind,
        string expectedDiagnostic)
    {
        // Npgsql and other non-HTTP clients surface transport failures as a SocketException nested
        // in a driver exception; they must land in the same four-way split as HttpClient does.
        var exception = new InvalidOperationException(
            "driver failure",
            new SocketException((int)socketError));

        var observation = NetworkFailureClassifier.FromException(exception, CancellationToken.None);

        Assert.Equal(expectedKind, observation.Kind);
        Assert.Null(observation.StatusCode);
        Assert.Equal(expectedDiagnostic, observation.Diagnostic);
    }

    [Fact]
    public void FromException_RejectsSocketFailuresOutsideTheSupportedSplit()
    {
        var exception = new SocketException((int)SocketError.AccessDenied);

        Assert.Throws<ArgumentException>(() =>
            NetworkFailureClassifier.FromException(exception, CancellationToken.None));
    }

    [Fact]
    public void FromException_KeepsCallerCancellationEvenWhenATransportFailureIsNested()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var exception = new OperationCanceledException(
            "caller cancelled",
            new SocketException((int)SocketError.ConnectionRefused),
            callerCancellation.Token);

        var rethrown = Assert.ThrowsAny<OperationCanceledException>(() =>
            NetworkFailureClassifier.FromException(exception, callerCancellation.Token));

        Assert.Same(exception, rethrown);
    }

    [Theory]
    [MemberData(nameof(TimeoutExceptions))]
    public void FromException_ClassifiesHelperOwnedCancellationAsRequestTimeout(Exception exception)
    {
        var observation = NetworkFailureClassifier.FromException(exception, CancellationToken.None);

        Assert.Equal(NetworkFailureKind.RequestTimeout, observation.Kind);
        Assert.Null(observation.StatusCode);
        Assert.Equal("Request timed out.", observation.Diagnostic);
    }

    [Fact]
    public void FromException_RethrowsCallerCancellationInsteadOfRewritingItAsTimeout()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var exception = new OperationCanceledException("caller cancelled", callerCancellation.Token);

        var rethrown = Assert.ThrowsAny<OperationCanceledException>(() =>
            NetworkFailureClassifier.FromException(exception, callerCancellation.Token));

        Assert.Same(exception, rethrown);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void FromResponse_KeepsPeerReportedTimeoutsOutOfBusinessErrors(HttpStatusCode statusCode)
    {
        using var response = new HttpResponseMessage(statusCode);

        var observation = NetworkFailureClassifier.FromResponse(response);

        Assert.Equal(NetworkFailureKind.RequestTimeout, observation.Kind);
        Assert.Equal(statusCode, observation.StatusCode);
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
