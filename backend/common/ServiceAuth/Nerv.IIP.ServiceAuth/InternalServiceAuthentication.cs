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
            builder.AddScheme<InternalServiceAuthenticationOptions, InternalServiceAuthenticationHandler>(
                SchemeName,
                options => options.BearerToken = ResolveBearerToken(configuration, environment));
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

        var authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        if (!TimeConstantEquals(token, configuredToken))
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

    private static bool TimeConstantEquals(string value, string expected)
    {
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(valueBytes, expectedBytes);
    }
}

public static class InternalServiceAuthorizationPolicy
{
    public const string Name = InternalServiceAuthentication.PolicyName;
}
