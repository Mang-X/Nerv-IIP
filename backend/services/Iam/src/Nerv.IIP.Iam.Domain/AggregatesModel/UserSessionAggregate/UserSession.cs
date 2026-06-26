using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.DomainEvents;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;

public partial record UserSessionId : IStringStronglyTypedId;

public class UserSession : Entity<UserSessionId>, IAggregateRoot
{
    private UserSession()
    {
        Id = new UserSessionId(string.Empty);
        UserId = new UserId(string.Empty);
    }

    public UserSession(
        UserSessionId id,
        UserId userId,
        string refreshTokenHash,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        int permissionVersion,
        string? clientInfo,
        string? ipAddress,
        string authenticationMethod = "password",
        string? externalProvider = null,
        string? externalSubject = null,
        DateTimeOffset? mfaVerifiedAtUtc = null,
        string? tokenFamilyId = null,
        string? previousSessionId = null)
    {
        Id = id;
        UserId = userId;
        RefreshTokenHash = refreshTokenHash;
        TokenFamilyId = string.IsNullOrWhiteSpace(tokenFamilyId) ? id.Id : tokenFamilyId;
        PreviousSessionId = previousSessionId;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        PermissionVersion = permissionVersion;
        ClientInfo = clientInfo;
        IpAddress = ipAddress;
        AuthenticationMethod = authenticationMethod;
        ExternalProvider = externalProvider;
        ExternalSubject = externalSubject;
        MfaVerifiedAtUtc = mfaVerifiedAtUtc;
        this.AddDomainEvent(new UserSessionCreatedDomainEvent(Id.Id, UserId.Id, issuedAtUtc));
    }

    public UserId UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    public string TokenFamilyId { get; private set; } = string.Empty;
    public string? PreviousSessionId { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public int PermissionVersion { get; private set; }
    public string? ClientInfo { get; private set; }
    public string? IpAddress { get; private set; }
    public string AuthenticationMethod { get; private set; } = "password";
    public string? ExternalProvider { get; private set; }
    public string? ExternalSubject { get; private set; }
    public DateTimeOffset? MfaVerifiedAtUtc { get; private set; }

    public bool CanRefresh(DateTimeOffset now)
    {
        return RevokedAtUtc is null && ExpiresAtUtc > now;
    }

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = now;
        RevokedReason = reason;
        this.AddDomainEvent(new UserSessionRevokedDomainEvent(Id.Id, UserId.Id, now, reason));
    }
}
