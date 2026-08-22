using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.ServiceAuth;

public static class PublicJwtAuthentication
{
    private const string DevelopmentPublicJwksJson = """
{"keys":[{"kty":"RSA","use":"sig","kid":"dev-rsa-2026-01","alg":"RS256","n":"tEYU0967vfBIQVtsmO87GsJUC_9PXED2hplI9VMnrKWW_5UO38OloycNOcVKFDUekblpr6YZ10SpdrkoyM9nENLoi8WYL5__VUCo96Dbd5oo7kanAi5m0FzvnY9a0Ax39TFTsUyBZ2G8alWMOkw1-BYJFtm8-z6j_kTlz93xe3griVcGyXTlNWi09pgvAC8Lj1ON42fovXiLjygnvCA5ZJeviMFZe43kftxjF0-fu0I6By6j-DyiIPGdHAIaSWn3cSl0Il2uBRmkW-aCs9GULHTs0Z3XpXklpQCc5dcn_UsFPGY5gIW-TbqqfBebZCZBROdgSnVrSNnIsdWRgplR9Q","e":"AQAB"}]}
""";
    private const string DefaultIssuer = "nerv-iip-iam";
    private const string DefaultAudience = "nerv-iip-api";

    public static IServiceCollection AddNervIipPublicJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var signingKeys = CreateSigningKeys(configuration, environment);
        var issuer = RequiredOutsideDevelopment(configuration, environment, "Iam:Jwt:Issuer", DefaultIssuer);
        var audience = RequiredOutsideDevelopment(configuration, environment, "Iam:Jwt:Audience", DefaultAudience);

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    IssuerSigningKeyResolver = (_, _, kid, _) => signingKeys
                        .Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        return services;
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
                throw new InvalidOperationException(
                    "Iam:Jwt:JwksJson is required for public JWT validation outside Development.");
            }

            jwksJson = DevelopmentPublicJwksJson;
        }

        IReadOnlyList<JsonWebKey> signingKeys;
        try
        {
            signingKeys = new JsonWebKeySet(jwksJson).Keys
                .Where(key => string.Equals(key.Kty, JsonWebAlgorithmsKeyTypes.RSA, StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Iam:Jwt:JwksJson must contain a valid public JWKS document.", exception);
        }

        if (signingKeys.Count == 0)
        {
            throw new InvalidOperationException("Iam:Jwt:JwksJson must contain at least one RSA public signing key.");
        }

        return signingKeys;
    }

    private static string RequiredOutsideDevelopment(
        IConfiguration configuration,
        IHostEnvironment environment,
        string key,
        string developmentDefault)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (environment.IsDevelopment())
        {
            return developmentDefault;
        }

        throw new InvalidOperationException($"{key} is required for public JWT validation outside Development.");
    }
}
