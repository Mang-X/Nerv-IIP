using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Web.Tests;
using DotNetCore.CAP;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ProductEngineeringStartupGovernanceTests
{
    [Fact]
    public async Task AutoMigrate_true_outside_development_is_rejected()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Persistence:AutoMigrate"] = "true",
        });

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health");
        });

        Assert.Contains(exception.Flatten(), x =>
            x is InvalidOperationException
            && x.Message.Contains("Persistence:AutoMigrate=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_analysis_endpoint_requires_internal_service_authorization()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/code-analysis");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure but received {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Code_analysis_endpoint_accepts_internal_service_token()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-service-token");

        var response = await client.GetAsync("/code-analysis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public async Task Business_endpoint_default_authentication_scheme_remains_jwt()
    {
        await using var factory = CreateFactory();

        var options = factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultScheme);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultAuthenticateScheme);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultChallengeScheme);
    }

    [Theory]
    [InlineData("iam", "nerv-iip-iam", "nerv-iip-api", true)]
    [InlineData("other", "nerv-iip-iam", "nerv-iip-api", false)]
    [InlineData("iam", "other-issuer", "nerv-iip-api", false)]
    [InlineData("iam", "nerv-iip-iam", "other-audience", false)]
    public async Task Iam_access_token_requires_matching_signature_issuer_and_audience(
        string signer, string issuer, string audience, bool expected)
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var rsa = RSA.Create(2048);
        if (signer == "iam")
        {
            rsa.ImportFromPem(IamJwtTestKeys.PrivateKeyPem);
        }

        var key = new RsaSecurityKey(rsa)
        {
            KeyId = IamJwtTestKeys.Kid,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var token = new JwtSecurityToken(
            issuer, audience, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {new JwtSecurityTokenHandler().WriteToken(token)}";

        var result = await context.AuthenticateAsync();

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Production_host_fails_to_start_without_iam_jwks()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Iam:Jwt:JwksJson", string.Empty);
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Iam:Jwt:JwksJson", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Testing_environment_does_not_register_cap_runtime()
    {
        await using var factory = CreateFactory();

        Assert.Empty(factory.Services.GetServices<ICapPublisher>());
    }

    [Fact]
    public async Task Scheduled_engineering_change_promotion_command_lock_is_registered()
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        var commandLocks = scope.ServiceProvider
            .GetServices<ICommandLock<PromoteScheduledEngineeringChangeCommand>>();

        Assert.Contains(commandLocks, commandLock => commandLock is PromoteScheduledEngineeringChangeCommandLock);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?>? overrides = null)
    {
        return ProductEngineeringWebTestFactory.Create("product-engineering-governance-http", overrides);
    }

}
