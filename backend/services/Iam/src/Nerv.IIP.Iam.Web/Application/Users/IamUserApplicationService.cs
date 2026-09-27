using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Microsoft.Extensions.Options;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Users;

public interface IIamUserApplicationService
{
    Task<PagedListResponse<UserResponse>> ListUsersAsync(IamListQueryOptions options, CancellationToken cancellationToken);

    Task<UserResponse> CreateUserAsync(
        string loginName,
        string email,
        string password,
        DateTimeOffset? accountExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<UserResponse> UpdateUserAsync(
        string userId,
        string loginName,
        string email,
        bool enabled,
        DateTimeOffset? accountExpiresAtUtc,
        CancellationToken cancellationToken);

    Task EnableUserAsync(string userId, CancellationToken cancellationToken);

    Task DisableUserAsync(string userId, CancellationToken cancellationToken);

    Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken);

    Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken);

    Task<UserMembershipResponse> GetMembershipAsync(
        string userId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 把用户在指定组织环境里的角色整组替换为 <paramref name="roleIds"/>；没有成员关系时新建，传空集合时移除成员关系。
    /// </summary>
    Task<UserMembershipResponse> ReplaceMembershipRolesAsync(
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyList<string> roleIds,
        SecurityAuditContext auditContext,
        CancellationToken cancellationToken);
}

public sealed class InMemoryIamUserApplicationService(
    InMemoryIamStore store,
    IOptions<IamPasswordPolicyOptions> passwordPolicyOptions) : IIamUserApplicationService
{
    public Task<PagedListResponse<UserResponse>> ListUsersAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var users = store.Users
            .Where(user => options.FilterEnabled is null || user.Enabled == options.FilterEnabled)
            .Where(user => string.IsNullOrWhiteSpace(options.FilterSearch)
                || user.UserId.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || user.LoginName.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase))
            .Select(ToResponse)
            .ApplyUserSort(options)
            .ToPagedResponse(options);
        return Task.FromResult(users);
    }

    public Task<UserResponse> CreateUserAsync(
        string loginName,
        string email,
        string password,
        DateTimeOffset? accountExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(ToResponse(store.CreateUser(
                loginName,
                email,
                password,
                accountExpiresAtUtc,
                ToStorePolicy(passwordPolicyOptions.Value))));
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }
    }

    public Task<UserResponse> UpdateUserAsync(
        string userId,
        string loginName,
        string email,
        bool enabled,
        DateTimeOffset? accountExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.UpdateUser(
            userId,
            loginName,
            email,
            enabled,
            accountExpiresAtUtc)));
    }

    public Task EnableUserAsync(string userId, CancellationToken cancellationToken)
    {
        store.EnableUser(userId);
        return Task.CompletedTask;
    }

    public Task DisableUserAsync(string userId, CancellationToken cancellationToken)
    {
        store.DisableUser(userId);
        return Task.CompletedTask;
    }

    public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new KnownException("New password is required.");
        }

        try
        {
            store.ResetPassword(userId, newPassword, ToStorePolicy(passwordPolicyOptions.Value));
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        try
        {
            store.ChangePassword(userId, currentPassword, newPassword, ToStorePolicy(passwordPolicyOptions.Value));
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new KnownException(ex.Message);
        }
        return Task.CompletedTask;
    }

    public Task<UserMembershipResponse> GetMembershipAsync(
        string userId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new UserMembershipResponse(
            userId,
            organizationId,
            environmentId,
            store.GetMembershipRoleIds(userId, organizationId, environmentId)));
    }

    public Task<UserMembershipResponse> ReplaceMembershipRolesAsync(
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyList<string> roleIds,
        SecurityAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(new UserMembershipResponse(
                userId,
                organizationId,
                environmentId,
                store.ReplaceMembershipRoles(userId, organizationId, environmentId, roleIds.Distinct(StringComparer.Ordinal).ToArray())));
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }
    }

    private static UserResponse ToResponse(UserFact user)
    {
        return new UserResponse(
            user.UserId,
            user.LoginName,
            user.Email,
            user.Enabled,
            user.AccountExpiresAtUtc,
            user.PasswordChangeRequired,
            user.PasswordExpiresAtUtc,
            user.LockoutUntilUtc);
    }

    private static InMemoryIamPasswordPolicy ToStorePolicy(IamPasswordPolicyOptions options)
    {
        return new InMemoryIamPasswordPolicy(
            options.MinimumLength,
            options.RequireUppercase,
            options.RequireLowercase,
            options.RequireDigit,
            options.RequireNonAlphanumeric,
            options.PasswordExpiresDays,
            options.PasswordHistoryCount);
    }
}

public sealed class PostgreSqlIamUserApplicationService(
    IUserRepository repository,
    IUserSessionRepository userSessionRepository,
    IMembershipRepository membershipRepository,
    IRoleRepository roleRepository,
    ISecurityAuditRecorder securityAudit,
    IamPasswordService passwordService,
    IamPasswordPolicy passwordPolicy) : IIamUserApplicationService
{
    public async Task<PagedListResponse<UserResponse>> ListUsersAsync(IamListQueryOptions options, CancellationToken cancellationToken)
    {
        var users = await repository.ListNotDeletedAsync(cancellationToken);
        return users
            .Where(user => options.FilterEnabled is null || user.Enabled == options.FilterEnabled)
            .Where(user => string.IsNullOrWhiteSpace(options.FilterSearch)
                || user.Id.Id.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || user.LoginName.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(options.FilterSearch, StringComparison.OrdinalIgnoreCase))
            .Select(ToResponse)
            .ApplyUserSort(options)
            .ToPagedResponse(options);
    }

    public async Task<UserResponse> CreateUserAsync(
        string loginName,
        string email,
        string password,
        DateTimeOffset? accountExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        passwordPolicy.ValidateComplexity(password);
        if (await repository.GetByLoginNameAsync(loginName, cancellationToken) is not null)
        {
            throw new KnownException($"Login name '{loginName}' is already used.");
        }

        if (await repository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new KnownException($"Email '{email}' is already used.");
        }

        var userId = new UserId($"user-{Guid.CreateVersion7():N}");
        var now = DateTimeOffset.UtcNow;
        var user = new User(
            userId,
            loginName,
            email,
            passwordService.Hash(password),
            true,
            Guid.NewGuid().ToString("n"),
            1,
            accountExpiresAtUtc,
            now,
            passwordPolicy.GetPasswordExpiresAtUtc(now),
            passwordChangeRequired: true);
        await repository.AddAsync(user, cancellationToken);
        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateUserAsync(
        string userId,
        string loginName,
        string email,
        bool enabled,
        DateTimeOffset? accountExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var typedUserId = new UserId(userId);
        var user = await repository.GetByIdAsync(typedUserId, cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");

        var userWithLoginName = await repository.GetByLoginNameAsync(loginName, cancellationToken);
        if (userWithLoginName is not null && userWithLoginName.Id != typedUserId)
        {
            throw new KnownException($"Login name '{loginName}' is already used.");
        }

        var userWithEmail = await repository.GetByEmailAsync(email, cancellationToken);
        if (userWithEmail is not null && userWithEmail.Id != typedUserId)
        {
            throw new KnownException($"Email '{email}' is already used.");
        }

        var shouldRevokeSessions = user.Enabled && !enabled;
        user.UpdateProfile(loginName, email, enabled, accountExpiresAtUtc);
        if (shouldRevokeSessions)
        {
            var now = DateTimeOffset.UtcNow;
            var sessions = await userSessionRepository.ListActiveByUserIdAsync(typedUserId, now, cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(now, "user-disabled");
            }
        }

        return ToResponse(user);
    }

    public async Task EnableUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(new UserId(userId), cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");
        user.Enable();
    }

    public async Task DisableUserAsync(string userId, CancellationToken cancellationToken)
    {
        var typedUserId = new UserId(userId);
        var user = await repository.GetByIdAsync(typedUserId, cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");
        user.Disable();
        var now = DateTimeOffset.UtcNow;
        var sessions = await userSessionRepository.ListActiveByUserIdAsync(typedUserId, now, cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now, "user-disabled");
        }
    }

    public async Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new KnownException("New password is required.");
        }

        var typedUserId = new UserId(userId);
        var user = await repository.GetByIdAsync(typedUserId, cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");
        passwordPolicy.ValidateNewPassword(user, newPassword);
        var now = DateTimeOffset.UtcNow;
        user.UpdatePasswordHash(
            passwordService.Hash(newPassword),
            now,
            passwordPolicy.GetPasswordExpiresAtUtc(now),
            passwordChangeRequired: true,
            passwordPolicy.Current.PasswordHistoryCount);

        var sessions = await userSessionRepository.ListActiveByUserIdAsync(typedUserId, now, cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now, "admin-password-reset");
        }
    }

    public async Task ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(new UserId(userId), cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");
        if (!passwordService.Verify(user, currentPassword))
        {
            throw new KnownException("Current password is invalid.");
        }

        passwordPolicy.ValidateNewPassword(user, newPassword);
        var now = DateTimeOffset.UtcNow;
        user.UpdatePasswordHash(
            passwordService.Hash(newPassword),
            now,
            passwordPolicy.GetPasswordExpiresAtUtc(now),
            passwordChangeRequired: false,
            passwordPolicy.Current.PasswordHistoryCount);
    }

    public async Task<UserMembershipResponse> GetMembershipAsync(
        string userId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var membership = await membershipRepository.GetByUserIdAndOrgEnvAsync(
            new UserId(userId),
            new OrganizationId(organizationId),
            new IamEnvironmentId(environmentId),
            cancellationToken);
        return new UserMembershipResponse(userId, organizationId, environmentId, RoleIdsOf(membership));
    }

    public async Task<UserMembershipResponse> ReplaceMembershipRolesAsync(
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyList<string> roleIds,
        SecurityAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        var typedUserId = new UserId(userId);
        _ = await repository.GetByIdAsync(typedUserId, cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");

        var desiredRoleIds = roleIds.Distinct(StringComparer.Ordinal).Select(x => new RoleId(x)).ToArray();
        var existingRoleIds = (await roleRepository.ListByIdsAsync(desiredRoleIds, cancellationToken))
            .Select(x => x.Id)
            .ToHashSet();
        var missingRoleId = desiredRoleIds.FirstOrDefault(x => !existingRoleIds.Contains(x));
        if (missingRoleId is not null)
        {
            throw new KnownException($"Role '{missingRoleId.Id}' was not found.");
        }

        var membership = await membershipRepository.GetByUserIdAndOrgEnvAsync(
            typedUserId,
            new OrganizationId(organizationId),
            new IamEnvironmentId(environmentId),
            cancellationToken);
        var before = RoleIdsOf(membership);
        if (desiredRoleIds.Length == 0)
        {
            if (membership is not null)
            {
                membershipRepository.Delete(membership);
            }
        }
        else if (membership is null)
        {
            membership = new Membership(
                new MembershipId($"membership-{Guid.CreateVersion7():N}"),
                typedUserId,
                new OrganizationId(organizationId),
                new IamEnvironmentId(environmentId),
                desiredRoleIds);
            await membershipRepository.AddAsync(membership, cancellationToken);
        }
        else
        {
            membership.ReplaceRoles(desiredRoleIds);
        }

        var after = desiredRoleIds.Select(x => x.Id).Order(StringComparer.Ordinal).ToArray();
        await securityAudit.RecordAsync(
            auditContext,
            "iam.membership.roles.changed",
            "user",
            userId,
            "success",
            new { organizationId, environmentId, before, after },
            DateTimeOffset.UtcNow,
            cancellationToken);
        return new UserMembershipResponse(userId, organizationId, environmentId, after);
    }

    private static IReadOnlyList<string> RoleIdsOf(Membership? membership) =>
        membership?.Roles.Select(x => x.RoleId.Id).Order(StringComparer.Ordinal).ToArray() ?? [];

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            user.Enabled,
            user.AccountExpiresAtUtc,
            user.PasswordChangeRequired,
            user.PasswordExpiresAtUtc,
            user.LockoutUntilUtc,
            user.DisplayName,
            user.EmployeeNo,
            user.DepartmentName);
    }
}

internal static class UserListSorting
{
    public static IEnumerable<UserResponse> ApplyUserSort(this IEnumerable<UserResponse> users, IamListQueryOptions options)
    {
        return (options.SortBy?.ToLowerInvariant(), options.IsDescending) switch
        {
            ("userid", true) => users.OrderByDescending(x => x.UserId, StringComparer.Ordinal),
            ("userid", false) => users.OrderBy(x => x.UserId, StringComparer.Ordinal),
            ("email", true) => users.OrderByDescending(x => x.Email, StringComparer.Ordinal),
            ("email", false) => users.OrderBy(x => x.Email, StringComparer.Ordinal),
            ("enabled", true) => users.OrderByDescending(x => x.Enabled).ThenBy(x => x.LoginName, StringComparer.Ordinal),
            ("enabled", false) => users.OrderBy(x => x.Enabled).ThenBy(x => x.LoginName, StringComparer.Ordinal),
            ("loginname", true) => users.OrderByDescending(x => x.LoginName, StringComparer.Ordinal),
            _ => users.OrderBy(x => x.LoginName, StringComparer.Ordinal)
        };
    }
}
