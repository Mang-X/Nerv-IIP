using Nerv.IIP.Iam.Domain.DomainEvents;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;

public partial record UserId : IStringStronglyTypedId;

public class User : Entity<UserId>, IAggregateRoot
{
    private User()
    {
        Id = new UserId(string.Empty);
    }

    public User(
        UserId id,
        string loginName,
        string email,
        string passwordHash,
        bool enabled,
        string securityStamp,
        int permissionVersion)
    {
        Id = id;
        LoginName = loginName;
        Email = email;
        PasswordHash = passwordHash;
        Enabled = enabled;
        SecurityStamp = securityStamp;
        PermissionVersion = permissionVersion;
    }

    public string LoginName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public int PermissionVersion { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public int FailedLoginCount { get; private set; }
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        LastLoginAtUtc = now;
        FailedLoginCount = 0;
        this.AddDomainEvent(new UserLoggedInDomainEvent(Id.Id, now));
    }

    public void RecordFailedLogin()
    {
        FailedLoginCount++;
    }

    public void Disable()
    {
        if (!Enabled)
        {
            return;
        }

        Enabled = false;
        RotateSecurityStamp();
        PermissionVersion++;
    }

    public void UpdateProfile(string loginName, string email, bool enabled)
    {
        LoginName = loginName;
        Email = email;

        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        RotateSecurityStamp();
        PermissionVersion++;
    }

    public void UpdatePasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        RotateSecurityStamp();
        PermissionVersion++;
    }

    private void RotateSecurityStamp()
    {
        SecurityStamp = Guid.NewGuid().ToString("n");
    }
}
