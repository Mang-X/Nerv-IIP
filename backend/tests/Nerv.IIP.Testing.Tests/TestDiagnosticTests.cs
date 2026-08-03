namespace Nerv.IIP.Testing.Tests;

public sealed class TestDiagnosticTests
{
    [Fact]
    public void Sanitize_RedactsExplicitSensitiveValues()
    {
        const string secret = "client-secret-value";

        var diagnostic = TestDiagnostic.Sanitize(
            $"request failed for {secret}",
            [secret, null, string.Empty]);

        Assert.Equal("request failed for [REDACTED]", diagnostic);
    }

    [Theory]
    [InlineData("password=hunter2", "password=[REDACTED]")]
    [InlineData("SECRET: open-sesame", "SECRET: [REDACTED]")]
    [InlineData("token=abc.def.ghi", "token=[REDACTED]")]
    [InlineData("credential: account-value", "credential: [REDACTED]")]
    [InlineData("apikey=api-key-value", "apikey=[REDACTED]")]
    [InlineData("api_key: api-key-value", "api_key: [REDACTED]")]
    [InlineData("connectionString=Host=db;Username=user;Password=pwd", "connectionString=[REDACTED]")]
    public void Sanitize_RedactsConfiguredKeysCaseInsensitively(string input, string expected)
    {
        Assert.Equal(expected, TestDiagnostic.Sanitize(input));
    }

    [Fact]
    public void Sanitize_DoesNotEchoRawHeadersOrRequestBodyValues()
    {
        var diagnostic = TestDiagnostic.Sanitize(
            "headers: Authorization=Bearer raw-header; body: token=raw-body; connectionString=Host=db;Password=pwd");

        Assert.DoesNotContain("raw-body", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=db", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("pwd", diagnostic, StringComparison.Ordinal);
    }
}
