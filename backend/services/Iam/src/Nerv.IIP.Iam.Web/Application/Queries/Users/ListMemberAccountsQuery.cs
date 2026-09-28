using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Queries.Users;

public sealed record ListMemberAccountsQuery(MemberAccountListOptions Options)
    : IQuery<PagedListResponse<MemberAccountResponse>>;

public sealed class ListMemberAccountsQueryHandler(IIamUserApplicationService users)
    : IQueryHandler<ListMemberAccountsQuery, PagedListResponse<MemberAccountResponse>>
{
    public async Task<PagedListResponse<MemberAccountResponse>> Handle(
        ListMemberAccountsQuery request,
        CancellationToken cancellationToken)
    {
        return await users.ListMemberAccountsAsync(request.Options, cancellationToken);
    }
}
