using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public static class GatewayPolicies
{
    public const string ConsoleAuthenticated = "gateway.console.authenticated";
}

public static class GatewayAuthentication
{
    private const string DevelopmentSigningKey = "nerv-iip-iam-development-signing-key-local-only-0001";
    private const string DefaultIssuer = "nerv-iip-iam";
    private const string DefaultAudience = "nerv-iip-api";

    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.TokenValidationParameters = CreateValidationParameters(configuration, environment);
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Nerv.IIP.PlatformGateway.Authentication");
                        logger.LogWarning(
                            context.Exception,
                            "GatewayJwtAuthenticationFailed Path={Path} FailureType={FailureType}",
                            context.HttpContext.Request.Path.ToString(),
                            context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Nerv.IIP.PlatformGateway.Authentication");
                        logger.LogWarning(
                            "GatewayJwtChallenge Path={Path} Error={Error} ErrorDescription={ErrorDescription}",
                            context.HttpContext.Request.Path.ToString(),
                            context.Error,
                            context.ErrorDescription);

                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.Headers.WWWAuthenticate = "Bearer";
                            await ResponseDataEndpointResults.WriteErrorAsync(
                                context.HttpContext,
                                StatusCodes.Status401Unauthorized,
                                "Unauthorized.",
                                context.HttpContext.RequestAborted);
                        }
                    },
                    OnForbidden = async context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Nerv.IIP.PlatformGateway.Authentication");
                        logger.LogWarning(
                            "GatewayJwtForbidden Path={Path}",
                            context.HttpContext.Request.Path.ToString());

                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await ResponseDataEndpointResults.WriteErrorAsync(
                                context.HttpContext,
                                StatusCodes.Status403Forbidden,
                                "Forbidden.",
                                context.HttpContext.RequestAborted);
                        }
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(GatewayPolicies.ConsoleAuthenticated, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            });

        return services;
    }

    private static TokenValidationParameters CreateValidationParameters(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Iam:Jwt:Issuer"] ?? DefaultIssuer,
            ValidateAudience = true,
            ValidAudience = configuration["Iam:Jwt:Audience"] ?? DefaultAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSigningKey(configuration, environment),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private static SymmetricSecurityKey CreateSigningKey(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var signingKey = configuration["Iam:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Iam:Jwt:SigningKey is required for Gateway JWT validation outside Development.");
            }

            signingKey = DevelopmentSigningKey;
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }
}
