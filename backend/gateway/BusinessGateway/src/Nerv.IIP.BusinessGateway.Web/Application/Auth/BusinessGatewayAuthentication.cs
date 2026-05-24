using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public static class BusinessGatewayPolicies
{
    public const string BusinessConsoleAuthenticated = "business-gateway.business-console.authenticated";
}

public static class BusinessGatewayAuthentication
{
    public static IServiceCollection AddBusinessGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwtSettings = BusinessGatewayJwtSettings.FromConfiguration(configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.TokenValidationParameters = CreateValidationParameters(jwtSettings);
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Nerv.IIP.BusinessGateway.Authentication");
                        logger.LogWarning(
                            context.Exception,
                            "BusinessGatewayJwtAuthenticationFailed Path={Path} FailureType={FailureType}",
                            context.HttpContext.Request.Path.ToString(),
                            context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Nerv.IIP.BusinessGateway.Authentication");
                        logger.LogWarning(
                            "BusinessGatewayJwtChallenge Path={Path} Error={Error} ErrorDescription={ErrorDescription}",
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
                            .CreateLogger("Nerv.IIP.BusinessGateway.Authentication");
                        logger.LogWarning(
                            "BusinessGatewayJwtForbidden Path={Path}",
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
            .AddPolicy(BusinessGatewayPolicies.BusinessConsoleAuthenticated, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            });

        return services;
    }

    private static TokenValidationParameters CreateValidationParameters(BusinessGatewayJwtSettings settings) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

    private sealed record BusinessGatewayJwtSettings(string SigningKey, string Issuer, string Audience)
    {
        public static BusinessGatewayJwtSettings FromConfiguration(IConfiguration configuration) =>
            new(
                Require(configuration, "Iam:Jwt:SigningKey"),
                Require(configuration, "Iam:Jwt:Issuer"),
                Require(configuration, "Iam:Jwt:Audience"));

        private static string Require(IConfiguration configuration, string key)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{key} is required for BusinessGateway JWT validation.");
            }

            return value;
        }
    }
}
