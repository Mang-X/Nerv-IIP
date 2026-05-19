using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Queries.Users;

public sealed record ListUsersQuery(IamListQueryOptions Options) : IQuery<PagedListResponse<UserResponse>>;

public sealed class ListUsersQueryHandler(IIamUserApplicationService users)
    : IQueryHandler<ListUsersQuery, PagedListResponse<UserResponse>>
{
    public async Task<PagedListResponse<UserResponse>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        return await users.ListUsersAsync(request.Options, cancellationToken);
    }
}
