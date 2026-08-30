using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.ServiceAuth;

public static class InternalServiceAuthentication
{
    public const string SchemeName = "InternalService";
    public const string PolicyName = "InternalService";
    public const string DefaultDevelopmentBearerToken = "local-internal-service-token";
    private const string AuthorizationOnlyDefaultSchemeName = "InternalService.AuthorizationOnly";

    public static IServiceCollection AddNervIipInternalServiceTokenProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IInternalServiceTokenProvider>(
            _ => new ConfigurationInternalServiceTokenProvider(configuration, environment));
        return services;
    }

    /// <summary>
    /// Registers internal-service authentication as the default authentication scheme.
    /// </summary>
    public static IServiceCollection AddNervIipInternalServiceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
        => AddNervIipInternalServiceCore(services, configuration, environment, useAsDefaultScheme: true);

    /// <summary>
    /// Adds the internal-service scheme and policy without replacing the service's existing default scheme.
    /// </summary>
    public static IServiceCollection AddNervIipInternalServiceAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
        => AddNervIipInternalServiceCore(services, configuration, environment, useAsDefaultScheme: false);

    private static IServiceCollection AddNervIipInternalServiceCore(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool useAsDefaultScheme)
    {
        services.AddNervIipInternalServiceTokenProvider(configuration, environment);

        var builder = useAsDefaultScheme
            ? services.AddAuthentication(SchemeName)
            : services.AddAuthentication();

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(InternalServiceAuthenticationSchemeRegistration)))
        {
            services.TryAddSingleton<InternalServiceAuthenticationSchemeRegistration>();
            services.AddOptions<InternalServiceAuthenticationOptions>(SchemeName)
                .Configure<IInternalServiceTokenProvider>((options, tokenProvider) =>
                    options.BearerToken = tokenProvider.BearerToken);
            builder.AddScheme<InternalServiceAuthenticationOptions, InternalServiceAuthenticationHandler>(
                SchemeName,
                _ => { });
            if (!useAsDefaultScheme)
            {
                builder.AddScheme<AuthenticationSchemeOptions, AuthorizationOnlyDefaultAuthenticationHandler>(
                    AuthorizationOnlyDefaultSchemeName,
                    _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    if (HasConfiguredDefaultScheme(options))
                    {
                        return;
                    }

                    // Keep unqualified authentication passive. The InternalService policy names its scheme,
                    // while a host default registered before or after this method remains authoritative.
                    options.DefaultScheme = AuthorizationOnlyDefaultSchemeName;
                });
            }
        }

        AddInternalServicePolicy(services);
        return services;
    }

    private static void AddInternalServicePolicy(IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(SchemeName);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("token_type", "internal_service");
            }));
    }

    private static bool HasConfiguredDefaultScheme(AuthenticationOptions options)
        => !string.IsNullOrWhiteSpace(options.DefaultScheme)
           || !string.IsNullOrWhiteSpace(options.DefaultAuthenticateScheme)
           || !string.IsNullOrWhiteSpace(options.DefaultChallengeScheme)
           || !string.IsNullOrWhiteSpace(options.DefaultForbidScheme)
           || !string.IsNullOrWhiteSpace(options.DefaultSignInScheme)
           || !string.IsNullOrWhiteSpace(options.DefaultSignOutScheme);

    internal static string ResolveBearerToken(IConfiguration configuration, IHostEnvironment environment)
    {
        var token = configuration["InternalService:BearerToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        if (environment.IsDevelopment())
        {
            return DefaultDevelopmentBearerToken;
        }

        throw new InvalidOperationException("InternalService:BearerToken is required outside Development.");
    }

    private sealed class InternalServiceAuthenticationSchemeRegistration;
}

internal sealed class AuthorizationOnlyDefaultAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}

public interface IInternalServiceTokenProvider
{
    string BearerToken { get; }
}

public sealed class ConfigurationInternalServiceTokenProvider : IInternalServiceTokenProvider
{
    public ConfigurationInternalServiceTokenProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        BearerToken = InternalServiceAuthentication.ResolveBearerToken(configuration, environment);
    }

    public string BearerToken { get; }
}

public sealed class InternalServiceAuthenticationOptions : AuthenticationSchemeOptions
{
    public string BearerToken { get; set; } = string.Empty;
}

public sealed class InternalServiceAuthenticationHandler(
    IOptionsMonitor<InternalServiceAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<InternalServiceAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredToken = Options.BearerToken;
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Internal service bearer token is not configured."));
        }

        if (!InternalServiceBearerToken.TryParse(Request.Headers.Authorization.ToString(), out var token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!InternalServiceBearerToken.FixedTimeEquals(token, configuredToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal service bearer token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "internal-service"),
            new Claim(ClaimTypes.Name, "internal-service"),
            new Claim("token_type", "internal_service")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public static class InternalServiceAuthorizationPolicy
{
    public const string Name = InternalServiceAuthentication.PolicyName;
}
