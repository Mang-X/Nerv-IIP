using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Iam.Web.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Quality.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class QualityStartupGovernanceTests
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
    public async Task Capa_automation_minimum_severity_configuration_is_validated_at_startup()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Quality:CapaAutomation:MinimumSeverity"] = "high",
        });

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health");
        });

        Assert.Contains(exception.Flatten(), x =>
            x is OptionsValidationException
            && x.Message.Contains("Quality:CapaAutomation:MinimumSeverity", StringComparison.Ordinal));
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
    public async Task Integration_event_dead_letter_store_uses_persistent_quality_db_store()
    {
        var database = $"quality-store-{Guid.CreateVersion7()}";
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
        }));
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var original = new IntegrationEventDeadLetterMessage(Guid.CreateVersion7(), "quality-store-probe", "original-event", "quality.test", 1,
            "business-quality", "original-key", "fixture", "{}", "original", "preserved failure", IntegrationEventDeadLetterStatus.Pending, at, null);
        using (var write = factory.Services.CreateScope())
            await write.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(original, CancellationToken.None);
        using (var read = factory.Services.CreateScope())
        {
            var row = Assert.Single(await read.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Set<IntegrationEventDeadLetter>().AsNoTracking().ToListAsync());
            Assert.Equal(original.Id, row.Id);
            Assert.Equal(original.EventId, row.EventId);
            Assert.Equal(original.FailureMessage, row.FailureMessage);
            await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>()
                .MarkReplayedAsync(original.Id, at, CancellationToken.None);
        }
        using var freshRead = factory.Services.CreateScope();
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, (await freshRead.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Set<IntegrationEventDeadLetter>().AsNoTracking().SingleAsync()).Status);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_governance;Username=nerv;Password=nerv",
            ["InternalService:BearerToken"] = "test-internal-service-token",
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Iam:Jwt:JwksJson", Nerv.IIP.Testing.IamJwtFixture.PublicJwksJson);
                builder.UseSetting("Quality:Seed:Enabled", "false"); // 产品基线 seed 默认开启（#3805），本用例不具备真实数据库
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
            });
    }

}
