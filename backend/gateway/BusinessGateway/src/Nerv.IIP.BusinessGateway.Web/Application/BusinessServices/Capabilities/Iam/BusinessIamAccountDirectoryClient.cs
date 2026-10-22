namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>IAM 成员账号：只含业务侧「关联登录账号」所需的最小字段。</summary>
public sealed record BusinessIamMemberAccount(
    string UserId,
    string LoginName,
    string? DisplayName,
    bool Enabled);

public sealed record BusinessIamMemberAccountPage(
    int PageIndex,
    int PageSize,
    int TotalCount,
    IReadOnlyList<BusinessIamMemberAccount> Items);

public sealed record BusinessIamMemberAccountListRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    IReadOnlyList<string>? UserIds = null,
    bool IncludeDisabled = false,
    int PageIndex = 1,
    int PageSize = 20);

/// <summary>
/// 读 IAM 组织/环境成员账号目录（<c>/internal/iam/v1/member-accounts</c>），供员工「关联登录账号」的候选、
/// 提交前核验与员工列表的登录名展示（#3924）。只读，不承载任何授权判断——授权仍由网关鉴权 client 完成。
/// </summary>
public interface IBusinessIamAccountDirectoryClient
{
    Task<BusinessIamMemberAccountPage> ListMemberAccountsAsync(
        string internalBearerToken,
        BusinessIamMemberAccountListRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessIamAccountDirectoryClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessIamAccountDirectoryClient
{
    public Task<BusinessIamMemberAccountPage> ListMemberAccountsAsync(
        string internalBearerToken,
        BusinessIamMemberAccountListRequest request,
        CancellationToken cancellationToken)
    {
        var query = JoinQuery(
            Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("keyword", request.Keyword),
                ("includeDisabled", TrueFlag(request.IncludeDisabled)),
                ("pageIndex", request.PageIndex),
                ("pageSize", request.PageSize)),
            RepeatedQuery("userIds", request.UserIds));
        return SendAsync<BusinessIamMemberAccountPage>(
            internalBearerToken,
            HttpMethod.Get,
            "/internal/iam/v1/member-accounts?" + query,
            null,
            cancellationToken);
    }
}
