using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Queries.Users;

public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserResponse>>;

public sealed class ListUsersQueryHandler(IIamUserApplicationService users)
    : IQueryHandler<ListUsersQuery, IReadOnlyList<UserResponse>>
{
    public async Task<IReadOnlyList<UserResponse>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        return await users.ListUsersAsync(cancellationToken);
    }
}
