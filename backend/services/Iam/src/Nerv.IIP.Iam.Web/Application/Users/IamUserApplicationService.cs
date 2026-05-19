using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Users;

public interface IIamUserApplicationService
{
    Task<IReadOnlyList<UserResponse>> ListUsersAsync(CancellationToken cancellationToken);

    Task<UserResponse> CreateUserAsync(string loginName, string email, string password, CancellationToken cancellationToken);

    Task<UserResponse> UpdateUserAsync(
        string userId,
        string loginName,
        string email,
        bool enabled,
        CancellationToken cancellationToken);

    Task DisableUserAsync(string userId, CancellationToken cancellationToken);
}

public sealed class InMemoryIamUserApplicationService(InMemoryIamStore store) : IIamUserApplicationService
{
    public Task<IReadOnlyList<UserResponse>> ListUsersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<UserResponse> users = store.Users
            .OrderBy(x => x.LoginName, StringComparer.Ordinal)
            .Select(ToResponse)
            .ToArray();
        return Task.FromResult(users);
    }

    public Task<UserResponse> CreateUserAsync(string loginName, string email, string password, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.CreateUser(loginName, email, password)));
    }

    public Task<UserResponse> UpdateUserAsync(
        string userId,
        string loginName,
        string email,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToResponse(store.UpdateUser(userId, loginName, email, enabled)));
    }

    public Task DisableUserAsync(string userId, CancellationToken cancellationToken)
    {
        store.DisableUser(userId);
        return Task.CompletedTask;
    }

    private static UserResponse ToResponse(UserFact user)
    {
        return new UserResponse(user.UserId, user.LoginName, user.Email, user.Enabled);
    }
}

public sealed class PostgreSqlIamUserApplicationService(
    IUserRepository repository,
    IamPasswordService passwordService) : IIamUserApplicationService
{
    public async Task<IReadOnlyList<UserResponse>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await repository.ListNotDeletedAsync(cancellationToken);
        return users.Select(ToResponse).ToArray();
    }

    public async Task<UserResponse> CreateUserAsync(
        string loginName,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        if (await repository.GetByLoginNameAsync(loginName, cancellationToken) is not null)
        {
            throw new KnownException($"Login name '{loginName}' is already used.");
        }

        if (await repository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new KnownException($"Email '{email}' is already used.");
        }

        var userId = new UserId($"user-{Guid.CreateVersion7():N}");
        var user = new User(
            userId,
            loginName,
            email,
            passwordService.Hash(password),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        await repository.AddAsync(user, cancellationToken);
        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateUserAsync(
        string userId,
        string loginName,
        string email,
        bool enabled,
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

        user.UpdateProfile(loginName, email, enabled);
        return ToResponse(user);
    }

    public async Task DisableUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(new UserId(userId), cancellationToken)
            ?? throw new KnownException($"User '{userId}' was not found.");
        user.Disable();
    }

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse(user.Id.Id, user.LoginName, user.Email, user.Enabled);
    }
}
