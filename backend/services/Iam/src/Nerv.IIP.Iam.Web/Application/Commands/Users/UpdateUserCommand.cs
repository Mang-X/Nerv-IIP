using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record UpdateUserCommand(string UserId, string LoginName, string Email, bool Enabled) : ICommand<UserResponse>;

public sealed class UpdateUserCommandHandler(IServiceProvider services)
    : ICommandHandler<UpdateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        if (services.GetService<IUserRepository>() is null)
        {
            var updatedUser = services.GetRequiredService<InMemoryIamStore>().UpdateUser(
                request.UserId,
                request.LoginName,
                request.Email,
                request.Enabled);
            return ToResponse(updatedUser);
        }

        var repository = services.GetRequiredService<IUserRepository>();
        var userId = new UserId(request.UserId);
        var user = await repository.GetByIdAsync(userId, cancellationToken)
            ?? throw new KnownException($"User '{request.UserId}' was not found.");

        var userWithLoginName = await repository.GetByLoginNameAsync(request.LoginName, cancellationToken);
        if (userWithLoginName is not null && userWithLoginName.Id != userId)
        {
            throw new KnownException($"Login name '{request.LoginName}' is already used.");
        }

        var userWithEmail = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (userWithEmail is not null && userWithEmail.Id != userId)
        {
            throw new KnownException($"Email '{request.Email}' is already used.");
        }

        user.UpdateProfile(request.LoginName, request.Email, request.Enabled);
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
