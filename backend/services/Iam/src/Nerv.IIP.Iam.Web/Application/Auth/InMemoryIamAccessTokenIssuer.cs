using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class InMemoryIamAccessTokenIssuer(IamTokenService tokenService) : IInMemoryIamAccessTokenIssuer
{
    public string CreateAccessToken(InMemoryIamAccessTokenIssue issue)
    {
        return tokenService.CreateAccessToken(
            issue.UserId,
            issue.SessionId,
            issue.SecurityStamp,
            issue.PermissionVersion,
            issue.LoginName,
            issue.Email,
            issue.OrganizationId,
            issue.EnvironmentId);
    }

    public DateTimeOffset GetAccessTokenExpiresAtUtc(DateTimeOffset issuedAtUtc)
    {
        return tokenService.GetAccessTokenExpiresAtUtc(issuedAtUtc);
    }
}
