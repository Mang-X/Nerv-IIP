using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Auth;
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
}

public sealed class InMemoryIamUserApplicationService(InMemoryIamStore store) : IIamUserApplicationService
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
            return Task.FromResult(ToResponse(store.CreateUser(loginName, email, password, accountExpiresAtUtc)));
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
        return Task.FromResult(ToResponse(store.UpdateUser(userId, loginName, email, enabled, accountExpiresAtUtc)));
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
            store.ResetPassword(userId, newPassword);
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
            store.ChangePassword(userId, currentPassword, newPassword);
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
}

public sealed class PostgreSqlIamUserApplicationService(
    IUserRepository repository,
    IUserSessionRepository userSessionRepository,
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

        user.UpdateProfile(loginName, email, enabled, accountExpiresAtUtc);
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
            user.LockoutUntilUtc);
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
