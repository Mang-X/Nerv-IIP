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
        int permissionVersion,
        DateTimeOffset? accountExpiresAtUtc = null,
        DateTimeOffset? passwordChangedAtUtc = null,
        DateTimeOffset? passwordExpiresAtUtc = null,
        bool passwordChangeRequired = false)
    {
        Id = id;
        LoginName = loginName;
        Email = email;
        PasswordHash = passwordHash;
        Enabled = enabled;
        SecurityStamp = securityStamp;
        PermissionVersion = permissionVersion;
        AccountExpiresAtUtc = accountExpiresAtUtc;
        PasswordChangedAtUtc = passwordChangedAtUtc;
        PasswordExpiresAtUtc = passwordExpiresAtUtc;
        PasswordChangeRequired = passwordChangeRequired;
    }

    private readonly List<UserPasswordHistory> passwordHistory = [];

    public string LoginName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public int PermissionVersion { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public DateTimeOffset? LastFailedLoginAtUtc { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutUntilUtc { get; private set; }
    public DateTimeOffset? AccountExpiresAtUtc { get; private set; }
    public DateTimeOffset? PasswordChangedAtUtc { get; private set; }
    public DateTimeOffset? PasswordExpiresAtUtc { get; private set; }
    public bool PasswordChangeRequired { get; private set; }
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
    public IReadOnlyCollection<UserPasswordHistory> PasswordHistory => passwordHistory;

    public bool IsLockedOut(DateTimeOffset now)
    {
        return LockoutUntilUtc is not null && LockoutUntilUtc > now;
    }

    public bool IsAccountExpired(DateTimeOffset now)
    {
        return AccountExpiresAtUtc is not null && AccountExpiresAtUtc <= now;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        LastLoginAtUtc = now;
        FailedLoginCount = 0;
        LastFailedLoginAtUtc = null;
        LockoutUntilUtc = null;
        this.AddDomainEvent(new UserLoggedInDomainEvent(Id.Id, now));
    }

    public void RecordFailedLogin(DateTimeOffset now, int lockoutThreshold, TimeSpan lockoutWindow)
    {
        if (lockoutThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lockoutThreshold), "Lockout threshold must be positive.");
        }

        if (lockoutWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockoutWindow), "Lockout window must be positive.");
        }

        LastFailedLoginAtUtc = now;
        FailedLoginCount++;
        // The failed attempt that reaches the threshold creates the lockout;
        // subsequent authentication attempts are rejected by IsLockedOut.
        if (FailedLoginCount >= lockoutThreshold)
        {
            LockoutUntilUtc = now.Add(lockoutWindow);
        }
    }

    public void Enable()
    {
        if (Enabled)
        {
            return;
        }

        Enabled = true;
        RotateSecurityStamp();
        PermissionVersion++;
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

    public void UpdateProfile(string loginName, string email, bool enabled, DateTimeOffset? accountExpiresAtUtc)
    {
        LoginName = loginName;
        Email = email;
        AccountExpiresAtUtc = accountExpiresAtUtc;

        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        RotateSecurityStamp();
        PermissionVersion++;
    }

    public void UpdatePasswordHash(
        string passwordHash,
        DateTimeOffset changedAtUtc,
        DateTimeOffset? passwordExpiresAtUtc,
        bool passwordChangeRequired,
        int historyLimit)
    {
        if (!string.IsNullOrWhiteSpace(PasswordHash))
        {
            passwordHistory.Add(new UserPasswordHistory(Id, PasswordHash, changedAtUtc));
            TrimPasswordHistory(historyLimit);
        }

        PasswordHash = passwordHash;
        PasswordChangedAtUtc = changedAtUtc;
        PasswordExpiresAtUtc = passwordExpiresAtUtc;
        PasswordChangeRequired = passwordChangeRequired;
        RotateSecurityStamp();
        PermissionVersion++;
    }

    private void TrimPasswordHistory(int historyLimit)
    {
        if (historyLimit < 1)
        {
            passwordHistory.Clear();
            return;
        }

        while (passwordHistory.Count > historyLimit)
        {
            var oldest = passwordHistory.OrderBy(x => x.CreatedAtUtc).First();
            passwordHistory.Remove(oldest);
        }
    }

    private void RotateSecurityStamp()
    {
        SecurityStamp = Guid.NewGuid().ToString("n");
    }
}

public partial record UserPasswordHistoryId : IGuidStronglyTypedId;

public class UserPasswordHistory : Entity<UserPasswordHistoryId>
{
    private UserPasswordHistory()
    {
        UserId = new UserId(string.Empty);
    }

    public UserPasswordHistory(UserId userId, string passwordHash, DateTimeOffset createdAtUtc)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
    }

    public UserId UserId { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
