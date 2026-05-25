using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Iam.Domain;

namespace Nerv.IIP.Iam.Infrastructure;

public sealed class InMemoryIamStore
{
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

    public InMemoryIamStore()
    {
        Seed();
    }

    public AuthResult Login(string loginName, string password)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(x => x.LoginName == loginName && x.Enabled && x.PasswordHash == Hash(password));
            if (user is null)
            {
                throw new UnauthorizedAccessException("Invalid login.");
            }

            return CreateSession(user);
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
            if (!user.Enabled)
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

    public UserFact ValidateAccessToken(string token)
    {
        lock (_gate)
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('|');
            if (parts.Length != 4)
            {
                throw new UnauthorizedAccessException("Invalid access token.");
            }

            if (!long.TryParse(parts[3], out var expiresAtUnixTimeSeconds))
            {
                throw new UnauthorizedAccessException("Invalid access token.");
            }

            if (DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixTimeSeconds) <= DateTimeOffset.UtcNow)
            {
                throw new UnauthorizedAccessException("Access token expired.");
            }

            var session = _sessions.SingleOrDefault(x => x.SessionId == parts[0] && x.RevokedAtUtc is null)
                ?? throw new UnauthorizedAccessException("Session revoked.");
            var user = _users.Single(x => x.UserId == session.UserId);
            if (!user.Enabled || user.SecurityStamp != parts[1] || user.PermissionVersion.ToString() != parts[2])
            {
                throw new UnauthorizedAccessException("Stale access token.");
            }

            return user;
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
            if (!user.Enabled || user.SecurityStamp != securityStamp || user.PermissionVersion != permissionVersion)
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
            var credential = _connectorHostCredentials.SingleOrDefault(x => x.ConnectorHostId == connectorHostId && x.SecretHash == Hash(secret) && x.ValidFromUtc <= DateTimeOffset.UtcNow && (x.ValidToUtc is null || x.ValidToUtc > DateTimeOffset.UtcNow))
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
                    && x.SecretHash == Hash(clientSecret)
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
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return _authorizationGrants.Any(x =>
                x.PrincipalType == "external-client"
                && x.PrincipalId == clientId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.PermissionCode == permissionCode
                && x.ValidFromUtc <= now
                && (x.ValidToUtc is null || x.ValidToUtc > now)
                && x.RevokedAtUtc is null);
        }
    }

    public UserFact CreateUser(string loginName, string email, string password)
    {
        lock (_gate)
        {
            EnsureUserIsUnique(null, loginName, email);

            var user = new UserFact(
                $"user-{Guid.NewGuid():N}",
                loginName,
                email,
                Hash(password),
                true,
                Guid.NewGuid().ToString("n"),
                1);
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

    public UserFact UpdateUser(string userId, string loginName, string email, bool enabled)
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
                Enabled = enabled,
                SecurityStamp = current.Enabled == enabled ? current.SecurityStamp : Guid.NewGuid().ToString("n"),
                PermissionVersion = current.Enabled == enabled ? current.PermissionVersion : current.PermissionVersion + 1
            };
            _users[index] = updated;
            return updated;
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

            _users[_users.IndexOf(user)] = user with
            {
                PasswordHash = Hash(newPassword),
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

    public IReadOnlyList<UserFact> Users => _users;
    public IReadOnlyList<RoleFact> Roles => _roles;
    public IReadOnlyList<UserSessionFact> Sessions => _sessions;

    private AuthResult CreateSession(UserFact user)
    {
        var sessionId = Guid.NewGuid().ToString("n");
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddMinutes(15);
        var session = new UserSessionFact(sessionId, user.UserId, Hash(refreshToken), now, now.AddDays(14), null, user.PermissionVersion);
        _sessions.Add(session);
        var membership = _memberships
            .OrderBy(x => x.OrganizationId, StringComparer.Ordinal)
            .ThenBy(x => x.EnvironmentId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.UserId == user.UserId);
        var accessToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{sessionId}|{user.SecurityStamp}|{user.PermissionVersion}|{expiresAtUtc.ToUnixTimeSeconds()}"));
        return new AuthResult(
            accessToken,
            refreshToken,
            sessionId,
            expiresAtUtc,
            user.UserId,
            user.SecurityStamp,
            user.PermissionVersion,
            user.LoginName,
            user.Email,
            membership?.OrganizationId,
            membership?.EnvironmentId);
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
        _users.Add(new UserFact("user-admin", "admin", "admin@nerv-iip.local", Hash("Admin123!"), true, Guid.NewGuid().ToString("n"), 1));
        _memberships.Add(new MembershipFact("user-admin", "org-001", "env-dev", new HashSet<string> { "role-platform-admin" }));
        _connectorHostCredentials.Add(new ConnectorHostCredentialFact("connector-host-001", "org-001", "env-dev", new HashSet<string>(NervIipSeedPermissions.All.Where(x => x.StartsWith("connectors.", StringComparison.Ordinal))), Hash("local-connector-secret"), DateTimeOffset.UtcNow.AddDays(-1), null));
        _externalClients.Add(new ExternalClientFact("external-client-demo", "Demo External Client", "org-001", "env-dev", Hash("external-client-secret"), true, 1, DateTimeOffset.UtcNow.AddDays(-1), null));
        _authorizationGrants.Add(new AuthorizationGrantFact("external-client", "external-client-demo", "org-001", "env-dev", "ops.tasks.create", DateTimeOffset.UtcNow.AddDays(-1), null, null));
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

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
    string? EnvironmentId);
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
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    DateTimeOffset? RevokedAtUtc);
