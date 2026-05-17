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
        string? ipAddress)
    {
        Id = id;
        UserId = userId;
        RefreshTokenHash = refreshTokenHash;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        PermissionVersion = permissionVersion;
        ClientInfo = clientInfo;
        IpAddress = ipAddress;
        this.AddDomainEvent(new UserSessionCreatedDomainEvent(Id.Id, UserId.Id, issuedAtUtc));
    }

    public UserId UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public int PermissionVersion { get; private set; }
    public string? ClientInfo { get; private set; }
    public string? IpAddress { get; private set; }

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
