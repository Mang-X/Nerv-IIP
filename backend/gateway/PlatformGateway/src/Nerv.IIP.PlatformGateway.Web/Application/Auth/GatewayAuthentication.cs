using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public static class GatewayPolicies
{
    public const string ConsoleAuthenticated = "gateway.console.authenticated";
}

public static class GatewayAuthentication
{
    private const string DevelopmentPublicJwksJson = """
{"keys":[{"kty":"RSA","use":"sig","kid":"dev-rsa-2026-01","alg":"RS256","n":"tEYU0967vfBIQVtsmO87GsJUC_9PXED2hplI9VMnrKWW_5UO38OloycNOcVKFDUekblpr6YZ10SpdrkoyM9nENLoi8WYL5__VUCo96Dbd5oo7kanAi5m0FzvnY9a0Ax39TFTsUyBZ2G8alWMOkw1-BYJFtm8-z6j_kTlz93xe3griVcGyXTlNWi09pgvAC8Lj1ON42fovXiLjygnvCA5ZJeviMFZe43kftxjF0-fu0I6By6j-DyiIPGdHAIaSWn3cSl0Il2uBRmkW-aCs9GULHTs0Z3XpXklpQCc5dcn_UsFPGY5gIW-TbqqfBebZCZBROdgSnVrSNnIsdWRgplR9Q","e":"AQAB"}]}
""";
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
        var signingKeys = CreateSigningKeys(configuration, environment);
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Iam:Jwt:Issuer"] ?? DefaultIssuer,
            ValidateAudience = true,
            ValidAudience = configuration["Iam:Jwt:Audience"] ?? DefaultAudience,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeyResolver = (_, _, kid, _) => signingKeys
                .Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private static IReadOnlyList<JsonWebKey> CreateSigningKeys(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwksJson = configuration["Iam:Jwt:JwksJson"];
        if (string.IsNullOrWhiteSpace(jwksJson))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Iam:Jwt:JwksJson is required for Gateway JWT validation outside Development.");
            }

            jwksJson = DevelopmentPublicJwksJson;
        }

        return new JsonWebKeySet(jwksJson).Keys
            .Where(key => string.Equals(key.Kty, JsonWebAlgorithmsKeyTypes.RSA, StringComparison.Ordinal))
            .ToArray();
    }
}
