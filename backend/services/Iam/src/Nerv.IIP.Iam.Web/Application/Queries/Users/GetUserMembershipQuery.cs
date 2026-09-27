using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Queries.Users;

public sealed record GetUserMembershipQuery(string UserId, string OrganizationId, string EnvironmentId)
    : IQuery<UserMembershipResponse>;

public sealed class GetUserMembershipQueryHandler(IIamUserApplicationService users)
    : IQueryHandler<GetUserMembershipQuery, UserMembershipResponse>
{
    public async Task<UserMembershipResponse> Handle(GetUserMembershipQuery request, CancellationToken cancellationToken)
    {
        return await users.GetMembershipAsync(request.UserId, request.OrganizationId, request.EnvironmentId, cancellationToken);
    }
}
