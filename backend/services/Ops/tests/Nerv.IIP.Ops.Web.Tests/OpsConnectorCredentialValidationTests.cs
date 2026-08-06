using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Ops.Web.Application.Auth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Ops.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class OpsConnectorCredentialValidationTests
{
    [Fact]
    public async Task Typed_iam_client_applies_explicit_connection_and_request_budgets()
    {
        var capture = new PrimaryHandlerCaptureFilter();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Ops:IamClient:ConnectTimeout", "00:00:00.250");
                builder.UseSetting("Ops:IamClient:RequestTimeout", "00:00:00.500");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture));
            });

        var client = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IamOpsConnectorCredentialValidator));

        Assert.Equal(TimeSpan.FromMilliseconds(500), client.Timeout);
        var handler = Assert.IsType<SocketsHttpHandler>(capture.PrimaryHandler);
        Assert.Equal(TimeSpan.FromMilliseconds(250), handler.ConnectTimeout);
    }

    [Theory]
    [InlineData("Ops:IamClient:ConnectTimeout")]
    [InlineData("Ops:IamClient:RequestTimeout")]
    public void Iam_client_budgets_must_be_positive(string setting)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting(setting, "00:00:00"));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(setting, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("positive", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Transport_failures_are_classified_and_fail_closed(
        Exception failure,
        NetworkFailureKind expectedKind,
        string expectedLogKind)
    {
        var observation = NetworkFailureClassifier.FromException(failure, CancellationToken.None);
        using var handler = new ScriptedHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var client = CreateClient(handler);
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-unavailable", result.Reason);
        Assert.Equal(expectedKind, observation.Kind);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expectedLogKind, entry.Properties["FailureKind"]);
        Assert.Null(entry.Properties["StatusCode"]);
        Assert.Same(failure, entry.Exception);
        AssertSafeLog(entry, "request-secret");
    }

    [Fact]
    public async Task Client_owned_request_timeout_is_classified_and_finishes_within_budget()
    {
        using var handler = new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("The timeout script unexpectedly resumed.");
        });
        using var client = CreateClient(handler, TimeSpan.FromMilliseconds(500));
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);
        var stopwatch = Stopwatch.StartNew();

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        stopwatch.Stop();
        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-unavailable", result.Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Elapsed: {stopwatch.Elapsed}");
        Assert.Equal(
            NetworkFailureKind.RequestTimeout,
            NetworkFailureClassifier.FromException(
                new OperationCanceledException("client timeout"),
                CancellationToken.None).Kind);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("request-timeout", entry.Properties["FailureKind"]);
        Assert.Null(entry.Properties["StatusCode"]);
        Assert.IsAssignableFrom<OperationCanceledException>(entry.Exception);
        AssertSafeLog(entry, "request-secret");
    }

    [Fact]
    public async Task Http_503_is_classified_as_business_response_and_fails_closed_without_logging_body()
    {
        using var classifierResponse = CreateResponse(HttpStatusCode.ServiceUnavailable, "classifier-body-secret");
        var observation = NetworkFailureClassifier.FromResponse(classifierResponse);
        using var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.ServiceUnavailable, "iam-body-secret")));
        using var client = CreateClient(handler);
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-unavailable", result.Reason);
        Assert.Equal(NetworkFailureKind.BusinessError, observation.Kind);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, observation.StatusCode);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("business-response", entry.Properties["FailureKind"]);
        Assert.Equal("503", entry.Properties["StatusCode"]);
        AssertSafeLog(entry, "request-secret", "iam-body-secret", "classifier-body-secret");
    }

    [Fact]
    public async Task Http_401_remains_an_iam_rejection()
    {
        using var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var client = CreateClient(handler);
        var validator = new IamOpsConnectorCredentialValidator(
            client,
            new RecordingLogger<IamOpsConnectorCredentialValidator>());

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-rejected", result.Reason);
    }

    [Fact]
    public async Task Malformed_success_remains_an_invalid_iam_response()
    {
        using var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json")
            }));
        using var client = CreateClient(handler);
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-invalid-response", result.Reason);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("invalid-response", entry.Properties["FailureKind"]);
        Assert.Equal("200", entry.Properties["StatusCode"]);
        Assert.IsAssignableFrom<JsonException>(entry.Exception);
        AssertSafeLog(entry, "request-secret", "not-json");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"principalType":"","organizationId":"org-001","environmentId":"env-dev","connectorHostId":"connector-host-001"}""")]
    [InlineData("""{"principalType":"connector-host","organizationId":"org-001","environmentId":"env-dev","connectorHostId":"   "}""")]
    public async Task Success_without_a_complete_principal_fails_closed(string body)
    {
        using var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }));
        using var client = CreateClient(handler);
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-invalid-response", result.Reason);
        Assert.Null(result.PrincipalType);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("invalid-response", entry.Properties["FailureKind"]);
        Assert.Equal("200", entry.Properties["StatusCode"]);
        AssertSafeLog(entry, "request-secret");
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_an_iam_failure()
    {
        using var handler = new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
        {
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("The cancellation script unexpectedly resumed.");
        });
        using var client = CreateClient(handler, TimeSpan.FromSeconds(5));
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateAsync(CreateRequest("request-secret"), callerCancellation.Token));

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Empty(logger.Entries);
    }

    public static TheoryData<Exception, NetworkFailureKind, string> TransportFailures => new()
    {
        {
            new HttpRequestException(
                HttpRequestError.NameResolutionError,
                "transport-detail"),
            NetworkFailureKind.Dns,
            "dns"
        },
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.ConnectionRefused)),
            NetworkFailureKind.ConnectionRefused,
            "connection-refused"
        },

        // The production validator deliberately duplicates Nerv.IIP.Testing's classifier because a
        // shipped assembly cannot reference a test assembly. Every row below pins the two mirrors to
        // the same verdict, so a change on either side that is not carried across fails here rather
        // than drifting silently (docs/architecture/backend-test-determinism.md, "网络结果与预算").
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.HostNotFound)),
            NetworkFailureKind.Dns,
            "dns"
        },
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.TryAgain)),
            NetworkFailureKind.Dns,
            "dns"
        },
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.NoData)),
            NetworkFailureKind.Dns,
            "dns"
        },
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.NoRecovery)),
            NetworkFailureKind.Dns,
            "dns"
        },
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.TimedOut)),
            NetworkFailureKind.RequestTimeout,
            "request-timeout"
        },

        // 移除 HttpRequestError.ConnectionError 前置门的**唯一理由**就在这两行：socket 错误码才是
        // 权威信号，同一个 refused 可以挂在别的 HttpRequestError bucket 下（TLS 握手期、代理隧道
        // 建立期）。前置门还在时这两行会落到 transport-error，DNS / refused / timeout 的三分被抹平。
        {
            new HttpRequestException(
                HttpRequestError.SecureConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.ConnectionRefused)),
            NetworkFailureKind.ConnectionRefused,
            "connection-refused"
        },
        {
            new HttpRequestException(
                HttpRequestError.ProxyTunnelError,
                "transport-detail",
                new SocketException((int)SocketError.HostNotFound)),
            NetworkFailureKind.Dns,
            "dns"
        }
    };

    [Theory]
    [MemberData(nameof(CancellationsWrappingATransportError))]
    public async Task Helper_owned_cancellation_outranks_a_nested_transport_error_on_both_sides(
        Exception failure,
        NetworkFailureKind expectedKind,
        string expectedLogKind)
    {
        // 分类**优先级**的镜像行：caller token 未取消、但异常是 OperationCanceledException 且内层
        // 裹着一个 socket 错误码。两侧都必须让「取消/超时」胜出而不是读内层的 refused/HostNotFound：
        // 测试侧在 FromException 里取消判定前置于 socket 搜索；生产侧的 OperationCanceledException
        // catch 块根本不会进 ClassifyTransportFailure。任一侧改成先读内层错误码都会在此变红。
        Assert.Equal(expectedKind, NetworkFailureClassifier.FromException(failure, CancellationToken.None).Kind);

        using var handler = new ScriptedHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var client = CreateClient(handler);
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-unavailable", result.Reason);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expectedLogKind, entry.Properties["FailureKind"]);
        Assert.Null(entry.Properties["StatusCode"]);
        Assert.IsAssignableFrom<OperationCanceledException>(entry.Exception);
        AssertSafeLog(entry, "request-secret");
    }

    public static TheoryData<Exception, NetworkFailureKind, string> CancellationsWrappingATransportError => new()
    {
        {
            new OperationCanceledException(
                "helper timeout",
                new SocketException((int)SocketError.ConnectionRefused)),
            NetworkFailureKind.RequestTimeout,
            "request-timeout"
        },
        {
            new TaskCanceledException(
                "helper timeout",
                new SocketException((int)SocketError.HostNotFound)),
            NetworkFailureKind.RequestTimeout,
            "request-timeout"
        }
    };

    [Theory]
    [MemberData(nameof(UnclassifiedTransportFailures))]
    public async Task Transport_failures_outside_the_split_stay_transport_error_and_fail_closed(
        Exception failure,
        string expectedLogKind)
    {
        // default 分支的正向覆盖：上一条 Theory 的每一行都被分进四分法，那样一来「什么还落到
        // transport-error」就完全没有用例。两侧对「超出四分法」的表达不同——测试侧必须显式扩表
        // 所以抛 ArgumentException，生产侧必须继续 fail closed 所以记 transport-error——这里同时
        // 钉住两者，任一侧偷偷把新错误码归进四分法都会在此变红。
        Assert.Throws<ArgumentException>(() =>
            NetworkFailureClassifier.FromException(failure, CancellationToken.None));

        using var handler = new ScriptedHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var client = CreateClient(handler);
        var logger = new RecordingLogger<IamOpsConnectorCredentialValidator>();
        var validator = new IamOpsConnectorCredentialValidator(client, logger);

        var result = await validator.ValidateAsync(CreateRequest("request-secret"), CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("iam-unavailable", result.Reason);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expectedLogKind, entry.Properties["FailureKind"]);
        Assert.Null(entry.Properties["StatusCode"]);
        Assert.Same(failure, entry.Exception);
        AssertSafeLog(entry, "request-secret");
    }

    public static TheoryData<Exception, string> UnclassifiedTransportFailures => new()
    {
        // 完全没有 socket 异常可查：既不是 DNS，也没有可读的 socket 错误码。
        {
            new HttpRequestException(HttpRequestError.SecureConnectionError, "transport-detail"),
            "transport-error"
        },

        // 有 socket 异常，但错误码不在四分法里。
        {
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "transport-detail",
                new SocketException((int)SocketError.AccessDenied)),
            "transport-error"
        },
        {
            new HttpRequestException(
                HttpRequestError.ResponseEnded,
                "transport-detail",
                new SocketException((int)SocketError.ConnectionReset)),
            "transport-error"
        }
    };

    private static OpsConnectorCredentialValidationRequest CreateRequest(string secret) => new(
        "connector-host-001",
        secret,
        "org-001",
        "env-dev",
        "ops.operation-tasks.execute");

    private static HttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null) => new(handler)
    {
        BaseAddress = new Uri("http://iam.test"),
        Timeout = timeout ?? TimeSpan.FromMilliseconds(500)
    };

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body)
    };

    private static void AssertSafeLog(LogEntry entry, params string[] sensitiveValues)
    {
        Assert.All(entry.Properties.Keys, key => Assert.Contains(key, new[] { "FailureKind", "StatusCode" }));
        foreach (var sensitiveValue in sensitiveValues)
        {
            Assert.DoesNotContain(sensitiveValue, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                sensitiveValue,
                string.Join(';', entry.Properties.Select(x => $"{x.Key}={x.Value}")),
                StringComparison.Ordinal);
        }
    }
}

internal sealed class ScriptedHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => sendAsync(request, cancellationToken);
}

internal sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Nerv.IIP.Ops.Web.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> entries = [];
    public IReadOnlyList<LogEntry> Entries => entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
            .Where(x => x.Key != "{OriginalFormat}")
            .ToDictionary(x => x.Key, x => x.Value?.ToString());
        entries.Add(new LogEntry(formatter(state, exception), properties, exception));
    }
}

internal sealed record LogEntry(
    string Message,
    IReadOnlyDictionary<string, string?> Properties,
    Exception? Exception);

internal sealed class PrimaryHandlerCaptureFilter : IHttpMessageHandlerBuilderFilter
{
    public HttpMessageHandler? PrimaryHandler { get; private set; }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name == nameof(IamOpsConnectorCredentialValidator))
        {
            PrimaryHandler = builder.PrimaryHandler;
        }
    };
}
