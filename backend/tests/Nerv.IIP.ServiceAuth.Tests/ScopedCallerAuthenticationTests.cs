using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.ServiceAuth.Tests;

public sealed class ScopedCallerAuthenticationTests
{
    private const string Scheme = "ScopedInboundCaller";
    private const string Policy = "ScopedInboundWrite";
    private const string Permission = "internal.records.write";
    private const string Token = "caller-token-one-0123456789";

    [Fact]
    public async Task Valid_configuration_authenticates_and_issues_trusted_caller_claims()
    {
        await using var provider = Services(ValidConfiguration()).BuildServiceProvider();
        var context = Context(provider, $"Bearer {Token}");

        var result = await context.AuthenticateAsync(Scheme);

        Assert.True(result.Succeeded);
        Assert.Equal("gateway-finance", result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("gateway-finance", result.Principal.FindFirstValue(ScopedCallerClaimTypes.Subject));
        Assert.Equal("org-001", result.Principal.FindFirstValue(ScopedCallerClaimTypes.OrganizationId));
        Assert.Equal("env-prod", result.Principal.FindFirstValue(ScopedCallerClaimTypes.EnvironmentId));
        Assert.Contains(Permission, result.Principal.FindAll(ScopedCallerClaimTypes.Permission).Select(x => x.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic caller-token-one-0123456789")]
    [InlineData("Bearercaller-token-one-0123456789")]
    [InlineData("Bearer")]
    public async Task Missing_or_non_bearer_authorization_does_not_authenticate(string? authorization)
    {
        await using var provider = Services(ValidConfiguration()).BuildServiceProvider();
        var context = Context(provider, authorization);

        var result = await context.AuthenticateAsync(Scheme);

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Theory]
    [InlineData("caller-token-one-012345678X")]
    [InlineData("caller-token-one-0123456789-extra")]
    public async Task A_near_match_or_prefix_match_token_is_rejected(string suppliedToken)
    {
        await using var provider = Services(ValidConfiguration()).BuildServiceProvider();
        var context = Context(provider, $"Bearer {suppliedToken}");

        var result = await context.AuthenticateAsync(Scheme);

        Assert.False(result.Succeeded);
        Assert.False(result.None);
        Assert.Contains("Invalid", result.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Named_policy_accepts_only_the_configured_scheme_and_permission()
    {
        await using var provider = Services(ValidConfiguration()).BuildServiceProvider();
        var authenticated = await Context(provider, $"Bearer {Token}").AuthenticateAsync(Scheme);
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var allowed = await authorization.AuthorizeAsync(authenticated.Principal!, null, Policy);
        var wrongPermission = new ClaimsPrincipal(new ClaimsIdentity(
            authenticated.Principal!.Claims.Where(x => x.Type != ScopedCallerClaimTypes.Permission)
                .Append(new Claim(ScopedCallerClaimTypes.Permission, "business.other.write")),
            Scheme));

        Assert.True(allowed.Succeeded);
        Assert.False((await authorization.AuthorizeAsync(wrongPermission, null, Policy)).Succeeded);
    }

    [Fact]
    public async Task Real_http_pipeline_returns_200_for_the_named_caller_and_401_for_a_wrong_token()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddNervIipScopedCallerAuthentication(ValidConfiguration(), Scheme);
        builder.Services.AddNervIipScopedCallerPolicy(Policy, Scheme, Permission);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/secure", (HttpContext context) => context.User.FindFirstValue(ScopedCallerClaimTypes.Subject))
            .RequireAuthorization(Policy);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token);

        var allowed = await client.GetAsync("/secure");
        var subject = await allowed.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token-one-0123456789");
        var rejected = await client.GetAsync("/secure");

        Assert.Equal(StatusCodes.Status200OK, (int)allowed.StatusCode);
        Assert.Equal("gateway-finance", subject);
        Assert.Equal(StatusCodes.Status401Unauthorized, (int)rejected.StatusCode);
    }

    [Fact]
    public void Scoped_caller_registration_does_not_replace_an_existing_default_scheme()
    {
        var services = new ServiceCollection();
        services.AddAuthentication("ExistingScheme");
        services.AddNervIipScopedCallerAuthentication(ValidConfiguration(), Scheme);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal("ExistingScheme", options.DefaultScheme);
    }

    [Fact]
    public async Task Existing_generic_internal_service_scheme_keeps_its_default_and_claims()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InternalService:BearerToken"] = "existing-generic-token"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNervIipInternalServiceAuthentication(configuration, new StubHostEnvironment("Production"));
        await using var provider = services.BuildServiceProvider();

        var result = await Context(provider, "Bearer existing-generic-token")
            .AuthenticateAsync(InternalServiceAuthentication.SchemeName);
        var defaults = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.True(result.Succeeded);
        Assert.Equal("internal-service", result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("internal_service", result.Principal.FindFirstValue("token_type"));
        Assert.Equal(InternalServiceAuthentication.SchemeName, defaults.DefaultScheme);
    }

    [Fact]
    public async Task Different_tokens_for_the_same_subject_and_scope_are_valid()
    {
        var configuration = ValidConfiguration(
            ("Profiles:1:Name", "finance-secondary"),
            ("Profiles:1:BearerToken", "caller-token-two-0123456789"),
            ("Profiles:1:Subject", "gateway-finance"),
            ("Profiles:1:OrganizationId", "org-001"),
            ("Profiles:1:EnvironmentId", "env-prod"),
            ("Profiles:1:Permissions:0", Permission));

        using var host = Host(configuration);
        await host.StartAsync();

        var secondary = await Context(host.Services, "Bearer caller-token-two-0123456789")
            .AuthenticateAsync(Scheme);

        Assert.True(secondary.Succeeded);
        Assert.Equal("gateway-finance", secondary.Principal!.FindFirstValue(ScopedCallerClaimTypes.Subject));
    }

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public async Task Missing_invalid_or_ambiguous_configuration_fails_when_the_host_starts(
        (string Key, string? Value)[] overrides)
    {
        using var host = Host(ValidConfiguration(overrides));

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    public static TheoryData<(string Key, string? Value)[]> InvalidConfigurations => new()
    {
        new[] { ("__clear__", (string?)null) },
        new[] { ("Profiles:0:BearerToken", (string?)" ") },
        new[] { ("Profiles:0:Subject", (string?)" ") },
        new[] { ("Profiles:0:Subject", (string?)"unsafe subject") },
        new[] { ("Profiles:0:OrganizationId", (string?)" ") },
        new[] { ("Profiles:0:EnvironmentId", (string?)" ") },
        new[] { ("Profiles:0:Permissions:0", (string?)" ") },
        new[]
        {
            ("Profiles:1:Name", (string?)"finance-primary"),
            ("Profiles:1:BearerToken", (string?)"caller-token-two-0123456789"),
            ("Profiles:1:Subject", (string?)"another-subject"),
            ("Profiles:1:OrganizationId", (string?)"org-002"),
            ("Profiles:1:EnvironmentId", (string?)"env-prod"),
            ("Profiles:1:Permissions:0", (string?)Permission)
        },
        new[]
        {
            ("Profiles:1:Name", (string?)"finance-secondary"),
            ("Profiles:1:BearerToken", (string?)Token),
            ("Profiles:1:Subject", (string?)"another-subject"),
            ("Profiles:1:OrganizationId", (string?)"org-002"),
            ("Profiles:1:EnvironmentId", (string?)"env-prod"),
            ("Profiles:1:Permissions:0", (string?)Permission)
        }
    };

    private static IHost Host(IConfiguration configuration)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddNervIipScopedCallerAuthentication(configuration, Scheme);
        builder.Services.AddNervIipScopedCallerPolicy(Policy, Scheme, Permission);
        return builder.Build();
    }

    private static IServiceCollection Services(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNervIipScopedCallerAuthentication(configuration, Scheme);
        services.AddNervIipScopedCallerPolicy(Policy, Scheme, Permission);
        return services;
    }

    private static IConfiguration ValidConfiguration(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Profiles:0:Name"] = "finance-primary",
            ["Profiles:0:BearerToken"] = Token,
            ["Profiles:0:Subject"] = "gateway-finance",
            ["Profiles:0:OrganizationId"] = "org-001",
            ["Profiles:0:EnvironmentId"] = "env-prod",
            ["Profiles:0:Permissions:0"] = Permission
        };

        if (overrides.Any(x => x.Key == "__clear__"))
        {
            values.Clear();
        }
        else
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static DefaultHttpContext Context(IServiceProvider services, string? authorization)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return context;
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
