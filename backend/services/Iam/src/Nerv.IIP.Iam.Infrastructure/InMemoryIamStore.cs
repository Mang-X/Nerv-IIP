using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Iam.Domain;

namespace Nerv.IIP.Iam.Infrastructure;

public sealed class InMemoryIamStore
{
    private const string SecretHashPrefix = "hmac-sha256:v1:";
    private const string DevelopmentSecretPepper = "nerv-iip-development-secret-pepper";
    private readonly IInMemoryIamAccessTokenIssuer _accessTokenIssuer;
    private readonly object _gate = new();
    private readonly List<OrganizationFact> _organizations = [];
    private readonly List<IamEnvironmentFact> _environments = [];
    private readonly List<UserFact> _users = [];
    private readonly List<RoleFact> _roles = [];
    private readonly List<MembershipFact> _memberships = [];
    private readonly List<UserSessionFact> _sessions = [];
    private readonly List<ConnectorHostCredentialFact> _connectorHostCredentials = [];
    private readonly List<ExternalClientFact> _externalClients = [];
    private readonly List<AuthorizationGrantFact> _authorizationGrants = [];

    public InMemoryIamStore() : this(new UnconfiguredInMemoryIamAccessTokenIssuer())
    {
    }

    public InMemoryIamStore(IInMemoryIamAccessTokenIssuer accessTokenIssuer)
    {
        _accessTokenIssuer = accessTokenIssuer ?? throw new ArgumentNullException(nameof(accessTokenIssuer));
        Seed();
    }

    public AuthResult Login(string loginName, string password)
    {
        return Login(loginName, password, 5, TimeSpan.FromMinutes(15));
    }

    public AuthResult Login(string loginName, string password, int lockoutThreshold, TimeSpan lockoutWindow)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(x => x.LoginName == loginName && x.Enabled);
            if (user is null)
            {
                throw new UnauthorizedAccessException("Invalid login.");
            }

            var now = DateTimeOffset.UtcNow;
            if (user.AccountExpiresAtUtc is not null && user.AccountExpiresAtUtc <= now)
            {
                throw new UnauthorizedAccessException("Invalid login.");
            }

            if (user.LockoutUntilUtc is not null && user.LockoutUntilUtc > now)
            {
                throw new UnauthorizedAccessException("Invalid login.");
            }

            if (!Verify(password, user.PasswordHash))
            {
                var failedLoginCount = user.FailedLoginCount + 1;
                var updated = user with
                {
                    FailedLoginCount = failedLoginCount,
                    LastFailedLoginAtUtc = now,
                    LockoutUntilUtc = failedLoginCount >= lockoutThreshold ? now.Add(lockoutWindow) : null
                };
                _users[_users.IndexOf(user)] = updated;
                throw new UnauthorizedAccessException("Invalid login.");
            }

            var successful = user with
            {
                FailedLoginCount = 0,
                LastFailedLoginAtUtc = null,
                LockoutUntilUtc = null
            };
            _users[_users.IndexOf(user)] = successful;
            return CreateSession(successful);
        }
    }

    public AuthResult Refresh(string refreshToken)
    {
        lock (_gate)
        {
            var hash = Hash(refreshToken);
            var session = _sessions.SingleOrDefault(x => x.RefreshTokenHash == hash && x.RevokedAtUtc is null && x.ExpiresAtUtc > DateTimeOffset.UtcNow)
                ?? throw new UnauthorizedAccessException("Invalid refresh token.");
            var user = _users.Single(x => x.UserId == session.UserId);
            if (!user.Enabled || (user.AccountExpiresAtUtc is not null && user.AccountExpiresAtUtc <= DateTimeOffset.UtcNow))
            {
                throw new UnauthorizedAccessException("User disabled.");
            }

            RevokeSession(session.SessionId);
            return CreateSession(user);
        }
    }

    public void Logout(string sessionId)
    {
        lock (_gate)
        {
            RevokeSession(sessionId);
        }
    }

    public UserFact ValidateAccessTokenPrincipal(
        string sessionId,
        string userId,
        string securityStamp,
        int permissionVersion)
    {
        lock (_gate)
        {
            var session = _sessions.SingleOrDefault(x =>
                    x.SessionId == sessionId
                    && x.RevokedAtUtc is null
                    && x.ExpiresAtUtc > DateTimeOffset.UtcNow)
                ?? throw new UnauthorizedAccessException("Session revoked.");
            if (session.UserId != userId)
            {
                throw new UnauthorizedAccessException("Invalid access token.");
            }

            var user = _users.Single(x => x.UserId == session.UserId);
            if (!user.Enabled
                || (user.AccountExpiresAtUtc is not null && user.AccountExpiresAtUtc <= DateTimeOffset.UtcNow)
                || user.SecurityStamp != securityStamp
                || user.PermissionVersion != permissionVersion)
            {
                throw new UnauthorizedAccessException("Stale access token.");
            }

            return user;
        }
    }

    public ConnectorPrincipal ValidateConnectorHost(string connectorHostId, string secret)
    {
        lock (_gate)
        {
            var credential = _connectorHostCredentials.SingleOrDefault(x => x.ConnectorHostId == connectorHostId && Verify(secret, x.SecretHash) && x.ValidFromUtc <= DateTimeOffset.UtcNow && (x.ValidToUtc is null || x.ValidToUtc > DateTimeOffset.UtcNow))
                ?? throw new UnauthorizedAccessException("Invalid Connector Host credential.");
            return new ConnectorPrincipal("connector-host", credential.OrganizationId, credential.EnvironmentId, credential.ConnectorHostId);
        }
    }

    public ExternalClientPrincipal IssueExternalClientToken(string clientId, string clientSecret, string? scope)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var client = _externalClients.SingleOrDefault(x =>
                    x.ClientId == clientId
                    && Verify(clientSecret, x.SecretHash)
                    && x.Enabled
                    && x.ValidFromUtc <= now
                    && (x.ValidToUtc is null || x.ValidToUtc > now))
                ?? throw new UnauthorizedAccessException("Invalid external client credential.");

            var requestedPermissions = SplitScope(scope);
            var grantedPermissions = _authorizationGrants
                .Where(x => x.PrincipalType == "external-client"
                    && x.PrincipalId == client.ClientId
                    && x.OrganizationId == client.OrganizationId
                    && x.EnvironmentId == client.EnvironmentId
                    && x.ValidFromUtc <= now
                    && (x.ValidToUtc is null || x.ValidToUtc > now)
                    && x.RevokedAtUtc is null)
                .Select(x => x.PermissionCode)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            if (requestedPermissions.Count == 0)
            {
                requestedPermissions = grantedPermissions;
            }
            else if (!requestedPermissions.IsSubsetOf(grantedPermissions))
            {
                throw new UnauthorizedAccessException("Requested scope is not granted.");
            }

            return new ExternalClientPrincipal(
                client.ClientId,
                client.DisplayName,
                client.OrganizationId,
                client.EnvironmentId,
                client.PermissionVersion,
                requestedPermissions.Order(StringComparer.Ordinal).ToArray());
        }
    }

    public CurrentPrincipalSnapshot GetCurrentPrincipal(UserFact user)
    {
        lock (_gate)
        {
            var membership = _memberships
                .OrderBy(x => x.OrganizationId, StringComparer.Ordinal)
                .ThenBy(x => x.EnvironmentId, StringComparer.Ordinal)
                .FirstOrDefault(x => x.UserId == user.UserId)
                ?? throw new UnauthorizedAccessException("User has no membership.");

            return new CurrentPrincipalSnapshot(
                user.UserId,
                user.LoginName,
                user.Email,
                "user",
                membership.OrganizationId,
                membership.EnvironmentId,
                user.PermissionVersion,
                _roles
                    .Where(x => membership.RoleIds.Contains(x.RoleId))
                    .SelectMany(x => x.PermissionCodes)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public bool UserHasPermission(string userId, string organizationId, string environmentId, string permissionCode)
    {
        lock (_gate)
        {
            var membership = _memberships.SingleOrDefault(x =>
                x.UserId == userId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId);
            if (membership is null)
            {
                return false;
            }

            return _roles
                .Where(x => membership.RoleIds.Contains(x.RoleId))
                .Any(x => x.PermissionCodes.Contains(permissionCode));
        }
    }

    public bool ExternalClientHasPermission(string clientId, string organizationId, string environmentId, string permissionCode)
    {
        return ExternalClientHasPermission(clientId, organizationId, environmentId, permissionCode, null, null);
    }

    public bool ExternalClientHasPermission(
        string clientId,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? resourceType,
        string? resourceId)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var normalizedResourceType = NormalizeResourceScope(resourceType);
            var normalizedResourceId = NormalizeResourceScope(resourceId);
            return _authorizationGrants.Any(x =>
                x.PrincipalType == "external-client"
                && x.PrincipalId == clientId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.PermissionCode == permissionCode
                && (x.ResourceType == "*"
                    || (x.ResourceType == normalizedResourceType
                        && (x.ResourceId == "*" || x.ResourceId == normalizedResourceId)))
                && x.ValidFromUtc <= now
                && (x.ValidToUtc is null || x.ValidToUtc > now)
                && x.RevokedAtUtc is null);
        }
    }

    public bool UserHasMembership(string userId, string organizationId, string environmentId)
    {
        lock (_gate)
        {
            return UserHasMembershipCore(userId, organizationId, environmentId);
        }
    }

    public UserFact? FindUserByEmail(string email)
    {
        lock (_gate)
        {
            return _users.SingleOrDefault(x =>
                x.Enabled
                && string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));
        }
    }

    public AuthResult CreateEnterpriseSession(
        string userId,
        string organizationId,
        string environmentId,
        string authenticationMethod,
        string externalProvider,
        string externalSubject,
        DateTimeOffset? mfaVerifiedAtUtc)
    {
        lock (_gate)
        {
            var user = _users.Single(x => x.UserId == userId && x.Enabled);
            if (!UserHasMembershipCore(userId, organizationId, environmentId))
            {
                throw new UnauthorizedAccessException("User is not a member of the requested organization environment.");
            }

            return CreateSession(user, organizationId, environmentId, authenticationMethod, externalProvider, externalSubject, mfaVerifiedAtUtc);
        }
    }

    public UserFact CreateUser(string loginName, string email, string password, DateTimeOffset? accountExpiresAtUtc)
    {
        lock (_gate)
        {
            ValidateComplexity(password);
            EnsureUserIsUnique(null, loginName, email);

            var now = DateTimeOffset.UtcNow;
            var user = new UserFact(
                $"user-{Guid.NewGuid():N}",
                loginName,
                email,
                Hash(password),
                true,
                Guid.NewGuid().ToString("n"),
                1,
                AccountExpiresAtUtc: accountExpiresAtUtc,
                PasswordChangedAtUtc: now,
                PasswordExpiresAtUtc: now.AddDays(90),
                PasswordChangeRequired: true,
                PasswordHistoryHashes: []);
            _users.Add(user);
            return user;
        }
    }

    public RoleFact CreateRole(string roleName, IEnumerable<string> permissionCodes)
    {
        lock (_gate)
        {
            EnsureRoleNameIsUnique(null, roleName);

            var role = new RoleFact(
                $"role-{Guid.CreateVersion7():N}",
                roleName,
                permissionCodes.ToHashSet(StringComparer.Ordinal));
            _roles.Add(role);
            return role;
        }
    }

    public RoleFact ReplaceRolePermissions(string roleId, IEnumerable<string> permissionCodes)
    {
        lock (_gate)
        {
            var role = _roles.SingleOrDefault(x => x.RoleId == roleId);
            if (role is null)
            {
                throw new InvalidOperationException($"Role '{roleId}' was not found.");
            }

            var updated = role with { PermissionCodes = permissionCodes.ToHashSet(StringComparer.Ordinal) };
            _roles[_roles.IndexOf(role)] = updated;
            return updated;
        }
    }

    public UserFact UpdateUser(string userId, string loginName, string email, bool enabled, DateTimeOffset? accountExpiresAtUtc)
    {
        lock (_gate)
        {
            EnsureUserIsUnique(userId, loginName, email);

            var index = _users.FindIndex(x => x.UserId == userId);
            if (index < 0)
            {
                throw new InvalidOperationException($"User '{userId}' was not found.");
            }

            var current = _users[index];
            var updated = current with
            {
                LoginName = loginName,
                Email = email,
                AccountExpiresAtUtc = accountExpiresAtUtc,
                Enabled = enabled,
                SecurityStamp = current.Enabled == enabled ? current.SecurityStamp : Guid.NewGuid().ToString("n"),
                PermissionVersion = current.Enabled == enabled ? current.PermissionVersion : current.PermissionVersion + 1
            };
            _users[index] = updated;
            return updated;
        }
    }

    public void EnableUser(string userId)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(x => x.UserId == userId);
            if (user is null || user.Enabled)
            {
                return;
            }

            _users[_users.IndexOf(user)] = user with
            {
                Enabled = true,
                SecurityStamp = Guid.NewGuid().ToString("n"),
                PermissionVersion = user.PermissionVersion + 1
            };
        }
    }

    public void DisableUser(string userId)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(x => x.UserId == userId);
            if (user is null || !user.Enabled)
            {
                return;
            }

            _users[_users.IndexOf(user)] = user with
            {
                Enabled = false,
                SecurityStamp = Guid.NewGuid().ToString("n"),
                PermissionVersion = user.PermissionVersion + 1
            };

            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < _sessions.Count; i++)
            {
                var session = _sessions[i];
                if (session.UserId == userId && session.RevokedAtUtc is null && session.ExpiresAtUtc > now)
                {
                    _sessions[i] = session with { RevokedAtUtc = now };
                }
            }
        }
    }

    public void ResetPassword(string userId, string newPassword)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(x => x.UserId == userId);
            if (user is null)
            {
                throw new InvalidOperationException($"User '{userId}' was not found.");
            }

            ValidateNewPassword(user, newPassword);
            var now = DateTimeOffset.UtcNow;
            var history = AppendPasswordHistory(user, now);
            _users[_users.IndexOf(user)] = user with
            {
                PasswordHash = Hash(newPassword),
                PasswordChangedAtUtc = now,
                PasswordExpiresAtUtc = now.AddDays(90),
                PasswordChangeRequired = true,
                PasswordHistoryHashes = history,
                SecurityStamp = Guid.NewGuid().ToString("n"),
                PermissionVersion = user.PermissionVersion + 1
            };

            for (var i = 0; i < _sessions.Count; i++)
            {
                var session = _sessions[i];
                if (session.UserId == userId && session.RevokedAtUtc is null && session.ExpiresAtUtc > now)
                {
                    _sessions[i] = session with { RevokedAtUtc = now };
                }
            }
        }
    }

    public void ChangePassword(string userId, string currentPassword, string newPassword)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(x => x.UserId == userId)
                ?? throw new InvalidOperationException($"User '{userId}' was not found.");
            if (!Verify(currentPassword, user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Current password is invalid.");
            }

            ValidateNewPassword(user, newPassword);
            var now = DateTimeOffset.UtcNow;
            var history = AppendPasswordHistory(user, now);
            _users[_users.IndexOf(user)] = user with
            {
                PasswordHash = Hash(newPassword),
                PasswordChangedAtUtc = now,
                PasswordExpiresAtUtc = now.AddDays(90),
                PasswordChangeRequired = false,
                PasswordHistoryHashes = history,
                SecurityStamp = Guid.NewGuid().ToString("n"),
                PermissionVersion = user.PermissionVersion + 1
            };
        }
    }

    public IReadOnlyList<UserFact> Users => _users;
    public IReadOnlyList<RoleFact> Roles => _roles;
    public IReadOnlyList<UserSessionFact> Sessions => _sessions;

    private AuthResult CreateSession(UserFact user)
    {
        var sessionId = Guid.NewGuid().ToString("n");
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var session = new UserSessionFact(
            sessionId,
            user.UserId,
            Hash(refreshToken),
            now,
            now.AddDays(14),
            null,
            user.PermissionVersion,
            "password",
            null,
            null,
            null);
        _sessions.Add(session);
        var membership = _memberships
            .OrderBy(x => x.OrganizationId, StringComparer.Ordinal)
            .ThenBy(x => x.EnvironmentId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.UserId == user.UserId);
        var accessToken = _accessTokenIssuer.CreateAccessToken(new InMemoryIamAccessTokenIssue(
            user.UserId,
            sessionId,
            user.SecurityStamp,
            user.PermissionVersion,
            now,
            user.LoginName,
            user.Email,
            membership?.OrganizationId,
            membership?.EnvironmentId));
        return new AuthResult(
            accessToken,
            refreshToken,
            sessionId,
            _accessTokenIssuer.GetAccessTokenExpiresAtUtc(now),
            user.UserId,
            user.SecurityStamp,
            user.PermissionVersion,
            user.LoginName,
            user.Email,
            membership?.OrganizationId,
            membership?.EnvironmentId,
            user.PasswordChangeRequired || (user.PasswordExpiresAtUtc is not null && user.PasswordExpiresAtUtc <= now));
    }

    private AuthResult CreateSession(
        UserFact user,
        string organizationId,
        string environmentId,
        string authenticationMethod,
        string? externalProvider,
        string? externalSubject,
        DateTimeOffset? mfaVerifiedAtUtc)
    {
        var sessionId = Guid.NewGuid().ToString("n");
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(externalProvider)
            && !string.IsNullOrWhiteSpace(externalSubject))
        {
            RevokeActiveExternalSessions(externalProvider, externalSubject, now);
        }

        var session = new UserSessionFact(
            sessionId,
            user.UserId,
            Hash(refreshToken),
            now,
            now.AddDays(14),
            null,
            user.PermissionVersion,
            authenticationMethod,
            externalProvider,
            externalSubject,
            mfaVerifiedAtUtc);
        _sessions.Add(session);
        var accessToken = _accessTokenIssuer.CreateAccessToken(new InMemoryIamAccessTokenIssue(
            user.UserId,
            sessionId,
            user.SecurityStamp,
            user.PermissionVersion,
            now,
            user.LoginName,
            user.Email,
            organizationId,
            environmentId));
        return new AuthResult(
            accessToken,
            refreshToken,
            sessionId,
            _accessTokenIssuer.GetAccessTokenExpiresAtUtc(now),
            user.UserId,
            user.SecurityStamp,
            user.PermissionVersion,
            user.LoginName,
            user.Email,
            organizationId,
            environmentId,
            user.PasswordChangeRequired || (user.PasswordExpiresAtUtc is not null && user.PasswordExpiresAtUtc <= now));
    }

    private bool UserHasMembershipCore(string userId, string organizationId, string environmentId)
    {
        return _memberships.Any(x =>
            x.UserId == userId
            && x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId);
    }

    private void RevokeActiveExternalSessions(string externalProvider, string externalSubject, DateTimeOffset now)
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            var session = _sessions[i];
            if (session.ExternalProvider == externalProvider
                && session.ExternalSubject == externalSubject
                && session.RevokedAtUtc is null
                && session.ExpiresAtUtc > now)
            {
                _sessions[i] = session with { RevokedAtUtc = now };
            }
        }
    }

    private void RevokeSession(string sessionId)
    {
        var session = _sessions.SingleOrDefault(x => x.SessionId == sessionId);
        if (session is not null)
        {
            _sessions[_sessions.IndexOf(session)] = session with { RevokedAtUtc = DateTimeOffset.UtcNow };
        }
    }

    private void Seed()
    {
        _organizations.Add(new OrganizationFact("org-001", "Nerv IIP", "active"));
        _environments.Add(new IamEnvironmentFact("env-dev", "org-001", "Development", "active"));
        _roles.Add(new RoleFact("role-platform-admin", "Platform Administrator", NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal)));
        var now = DateTimeOffset.UtcNow;
        _users.Add(new UserFact(
            "user-admin",
            "admin",
            "admin@nerv-iip.local",
            Hash("Admin123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1,
            PasswordChangedAtUtc: now,
            PasswordExpiresAtUtc: now.AddDays(90),
            PasswordHistoryHashes: []));
        _memberships.Add(new MembershipFact("user-admin", "org-001", "env-dev", new HashSet<string> { "role-platform-admin" }));
        _connectorHostCredentials.Add(new ConnectorHostCredentialFact("connector-host-001", "org-001", "env-dev", new HashSet<string>(NervIipSeedPermissions.All.Where(x => x.StartsWith("connectors.", StringComparison.Ordinal))), Hash("local-connector-secret"), DateTimeOffset.UtcNow.AddDays(-1), null));
        _externalClients.Add(new ExternalClientFact("external-client-demo", "Demo External Client", "org-001", "env-dev", Hash("external-client-secret"), true, 1, DateTimeOffset.UtcNow.AddDays(-1), null));
        _authorizationGrants.Add(new AuthorizationGrantFact("external-client", "external-client-demo", "org-001", "env-dev", "ops.tasks.create", "*", "*", DateTimeOffset.UtcNow.AddDays(-1), null, null));
        _externalClients.Add(new ExternalClientFact("external-client-resource-demo", "Resource Scoped External Client", "org-001", "env-dev", Hash("external-client-resource-secret"), true, 1, DateTimeOffset.UtcNow.AddDays(-1), null));
        _authorizationGrants.Add(new AuthorizationGrantFact("external-client", "external-client-resource-demo", "org-001", "env-dev", "ops.tasks.create", "operation-template", "restart-critical", DateTimeOffset.UtcNow.AddDays(-1), null, null));
    }

    private void EnsureUserIsUnique(string? currentUserId, string loginName, string email)
    {
        if (_users.Any(x => x.UserId != currentUserId && string.Equals(x.LoginName, loginName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Login name '{loginName}' is already used.");
        }

        if (_users.Any(x => x.UserId != currentUserId && string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Email '{email}' is already used.");
        }
    }

    private static void ValidateNewPassword(UserFact user, string password)
    {
        ValidateComplexity(password);
        if (Verify(password, user.PasswordHash)
            || (user.PasswordHistoryHashes ?? []).Any(hash => Verify(password, hash)))
        {
            throw new InvalidOperationException("Password was recently used.");
        }
    }

    private static void ValidateComplexity(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 8
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || password.All(char.IsLetterOrDigit))
        {
            throw new InvalidOperationException("Password does not satisfy IAM password policy.");
        }
    }

    private static IReadOnlyList<string> AppendPasswordHistory(UserFact user, DateTimeOffset now)
    {
        _ = now;
        return (user.PasswordHistoryHashes ?? [])
            .Append(user.PasswordHash)
            .TakeLast(5)
            .ToArray();
    }

    public bool RoleNameExists(string roleName)
    {
        lock (_gate)
        {
            return _roles.Any(x => string.Equals(x.RoleName, roleName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void EnsureRoleNameIsUnique(string? currentRoleId, string roleName)
    {
        if (_roles.Any(x => x.RoleId != currentRoleId && string.Equals(x.RoleName, roleName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Role name '{roleName}' is already used.");
        }
    }

    private static string Hash(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(DevelopmentSecretPepper));
        return $"{SecretHashPrefix}{Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";
    }

    private static bool Verify(string value, string storedHash)
    {
        if (!storedHash.StartsWith(SecretHashPrefix, StringComparison.Ordinal)
            || storedHash.Length != SecretHashPrefix.Length + 64)
        {
            return false;
        }

        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(Hash(value)));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(storedHash));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static HashSet<string> SplitScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return [];
        }

        return scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeResourceScope(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
}

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    string SessionId,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string SecurityStamp,
    int PermissionVersion,
    string LoginName,
    string Email,
    string? OrganizationId,
    string? EnvironmentId,
    bool PasswordChangeRequired);
public sealed record ConnectorPrincipal(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
public sealed record ExternalClientPrincipal(
    string ClientId,
    string DisplayName,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion,
    IReadOnlyList<string> Scope);
public sealed record CurrentPrincipalSnapshot(
    string UserId,
    string LoginName,
    string Email,
    string PrincipalType,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion,
    IReadOnlyList<string> PermissionCodes);
public sealed record ExternalClientFact(
    string ClientId,
    string DisplayName,
    string OrganizationId,
    string EnvironmentId,
    string SecretHash,
    bool Enabled,
    int PermissionVersion,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc);
public sealed record AuthorizationGrantFact(
    string PrincipalType,
    string PrincipalId,
    string OrganizationId,
    string EnvironmentId,
    string PermissionCode,
    string ResourceType,
    string ResourceId,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    DateTimeOffset? RevokedAtUtc);
