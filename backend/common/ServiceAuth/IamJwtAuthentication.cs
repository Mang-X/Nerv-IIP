using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.ServiceAuth;

internal static class IamJwtAuthentication
{
    private const string DevelopmentPublicJwksJson = """
{"keys":[{"kty":"RSA","use":"sig","kid":"dev-rsa-2026-01","alg":"RS256","n":"tEYU0967vfBIQVtsmO87GsJUC_9PXED2hplI9VMnrKWW_5UO38OloycNOcVKFDUekblpr6YZ10SpdrkoyM9nENLoi8WYL5__VUCo96Dbd5oo7kanAi5m0FzvnY9a0Ax39TFTsUyBZ2G8alWMOkw1-BYJFtm8-z6j_kTlz93xe3griVcGyXTlNWi09pgvAC8Lj1ON42fovXiLjygnvCA5ZJeviMFZe43kftxjF0-fu0I6By6j-DyiIPGdHAIaSWn3cSl0Il2uBRmkW-aCs9GULHTs0Z3XpXklpQCc5dcn_UsFPGY5gIW-TbqqfBebZCZBROdgSnVrSNnIsdWRgplR9Q","e":"AQAB"}]}
""";

    public static void AddNervIipIamJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwksJson = configuration["Iam:Jwt:JwksJson"];
        if (string.IsNullOrWhiteSpace(jwksJson))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Iam:Jwt:JwksJson is required outside Development.");
            }

            jwksJson = DevelopmentPublicJwksJson;
        }

        var signingKeys = new JsonWebKeySet(jwksJson).Keys
            .Where(key => key.Kty == JsonWebAlgorithmsKeyTypes.RSA
                && !key.HasPrivateKey
                && !string.IsNullOrWhiteSpace(key.KeyId)
                && !string.IsNullOrWhiteSpace(key.N)
                && !string.IsNullOrWhiteSpace(key.E)
                && (string.IsNullOrEmpty(key.Alg) || key.Alg == SecurityAlgorithms.RsaSha256)
                && (string.IsNullOrEmpty(key.Use) || key.Use == "sig"))
            .ToArray();

        if (signingKeys.Length == 0)
        {
            throw new InvalidOperationException("Iam:Jwt:JwksJson must contain a public RS256 signing key.");
        }

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Iam:Jwt:Issuer"] ?? "nerv-iip-iam",
                    ValidateAudience = true,
                    ValidAudience = configuration["Iam:Jwt:Audience"] ?? "nerv-iip-api",
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    IssuerSigningKeyResolver = (_, _, kid, _) =>
                        signingKeys.Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
    }
}
