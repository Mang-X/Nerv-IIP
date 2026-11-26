using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

internal static class FileDownloadAuthorization
{
    public const string Policy = "FileDownload";
    public const string PlatformGateway = "platform-gateway";
    public const string BusinessGateway = "business-gateway";

    public static void AddFileDownloadAuthorization(this WebApplicationBuilder builder)
    {
        var authentication = builder.Services.AddAuthentication();
        foreach (var caller in new[] { PlatformGateway, BusinessGateway })
        {
            authentication.AddScheme<InternalServiceAuthenticationOptions, InternalServiceAuthenticationHandler>(
                caller, _ => { });
            builder.Services.AddOptions<InternalServiceAuthenticationOptions>(caller)
                .Configure<IConfiguration, IHostEnvironment, IInternalServiceTokenProvider>(
                    (options, configuration, environment, sharedToken) =>
                    {
                        var token = ResolveToken(configuration, environment, caller);
                        var otherCaller = caller == PlatformGateway ? BusinessGateway : PlatformGateway;
                        if (!string.IsNullOrEmpty(token)
                            && (token == sharedToken.BearerToken
                                || token == ResolveToken(configuration, environment, otherCaller)))
                        {
                            throw new InvalidOperationException("文件下载调用凭据必须分别配置，且不得与共享凭据重复。");
                        }

                        options.BearerToken = token ?? string.Empty;
                    })
                .ValidateOnStart();
        }

        builder.Services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(InternalServiceAuthentication.SchemeName, PlatformGateway, BusinessGateway);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("token_type", "internal_service");
        }));
    }

    public static string GetCaller(ClaimsPrincipal principal) =>
        principal.Identities.Single(identity => identity.IsAuthenticated).AuthenticationType!;

    private static string? ResolveToken(IConfiguration configuration, IHostEnvironment environment, string caller) =>
        configuration[$"FileStorage:DownloadCallers:{caller}:BearerToken"]
        ?? (environment.IsDevelopment() ? $"local-file-download-{caller}" : null);
}
