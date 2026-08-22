using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.ServiceAuth.Tests;

public sealed class PublicJwtAuthenticationTests
{
    [Theory]
    [InlineData("Iam:Jwt:JwksJson")]
    [InlineData("Iam:Jwt:Issuer")]
    [InlineData("Iam:Jwt:Audience")]
    public void Production_requires_complete_public_validation_material(string missingKey)
    {
        using var rsa = RSA.Create(2048);
        var settings = Settings(rsa).Where(entry => !string.Equals(entry.Key, missingKey, StringComparison.Ordinal));
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNervIipPublicJwtAuthentication(Configuration(settings), Environment("Production")));

        Assert.Contains(missingKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_public_key_accepts_matching_rs256_token()
    {
        using var rsa = RSA.Create(2048);
        var validationParameters = ValidationParameters(rsa);
        var token = Token(rsa, "nerv-iip-iam", "nerv-iip-api");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, validationParameters);

        Assert.True(result.IsValid, result.Exception?.ToString());
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    public async Task Wrong_key_issuer_or_audience_is_rejected(string mutation)
    {
        using var trustedRsa = RSA.Create(2048);
        using var wrongRsa = RSA.Create(2048);
        var signingKey = mutation == "wrong-key" ? wrongRsa : trustedRsa;
        var issuer = mutation == "wrong-issuer" ? "other-issuer" : "nerv-iip-iam";
        var audience = mutation == "wrong-audience" ? "other-audience" : "nerv-iip-api";

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            Token(signingKey, issuer, audience),
            ValidationParameters(trustedRsa));

        Assert.False(result.IsValid);
    }

    private static TokenValidationParameters ValidationParameters(RSA rsa)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNervIipPublicJwtAuthentication(
            Configuration(Settings(rsa)),
            Environment("Production"));
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;
    }

    private static string Token(RSA rsa, string issuer, string audience)
    {
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "iam-test-key" };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "test-user")]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        });
    }

    private static IReadOnlyList<KeyValuePair<string, string?>> Settings(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        var jwks = $$"""
{"keys":[{"kty":"RSA","use":"sig","kid":"iam-test-key","alg":"RS256","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
""";
        return
        [
            new("Iam:Jwt:JwksJson", jwks),
            new("Iam:Jwt:Issuer", "nerv-iip-iam"),
            new("Iam:Jwt:Audience", "nerv-iip-api")
        ];
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> entries) =>
        new ConfigurationBuilder().AddInMemoryCollection(entries).Build();

    private static IHostEnvironment Environment(string name) => new StubHostEnvironment { EnvironmentName = name };

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
