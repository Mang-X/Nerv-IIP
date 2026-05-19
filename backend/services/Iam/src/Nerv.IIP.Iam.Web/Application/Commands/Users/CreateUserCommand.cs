using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record CreateUserCommand(string LoginName, string Email, string Password) : ICommand<UserResponse>;

public sealed class CreateUserCommandHandler(IServiceProvider services, IamPasswordService passwordService)
    : ICommandHandler<CreateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<IUserRepository>() is null)
        {
            var createdUser = services.GetRequiredService<InMemoryIamStore>().CreateUser(
                request.LoginName,
                request.Email,
                request.Password);
            return ToResponse(createdUser);
        }

        var repository = services.GetRequiredService<IUserRepository>();
        if (await repository.GetByLoginNameAsync(request.LoginName, cancellationToken) is not null)
        {
            throw new KnownException($"Login name '{request.LoginName}' is already used.");
        }

        if (await repository.GetByEmailAsync(request.Email, cancellationToken) is not null)
        {
            throw new KnownException($"Email '{request.Email}' is already used.");
        }

        var userId = new UserId($"user-{Guid.CreateVersion7():N}");
        var passwordUser = new User(
            userId,
            request.LoginName,
            request.Email,
            string.Empty,
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var user = new User(
            userId,
            request.LoginName,
            request.Email,
            passwordService.Hash(passwordUser, request.Password),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        await repository.AddAsync(user, cancellationToken);
        return ToResponse(user);
    }

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse(user.Id.Id, user.LoginName, user.Email, user.Enabled);
    }

    private static UserResponse ToResponse(UserFact user)
    {
        return new UserResponse(user.UserId, user.LoginName, user.Email, user.Enabled);
    }
}
