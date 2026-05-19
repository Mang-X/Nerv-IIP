using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Queries.Users;

public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserResponse>>;

public sealed class ListUsersQueryHandler(IServiceProvider services)
    : IQueryHandler<ListUsersQuery, IReadOnlyList<UserResponse>>
{
    public async Task<IReadOnlyList<UserResponse>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        if (services.GetService<IUserRepository>() is null)
        {
            return services.GetRequiredService<InMemoryIamStore>()
                .Users
                .OrderBy(x => x.LoginName, StringComparer.Ordinal)
                .Select(ToResponse)
                .ToArray();
        }

        var users = await services.GetRequiredService<IUserRepository>().ListNotDeletedAsync(cancellationToken);
        return users.Select(ToResponse).ToArray();
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
