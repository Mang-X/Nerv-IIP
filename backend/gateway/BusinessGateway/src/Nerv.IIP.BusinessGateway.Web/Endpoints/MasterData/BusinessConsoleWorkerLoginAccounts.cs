using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

/// <summary>
/// 员工 ↔ 登录账号（#3924）在网关一侧的两件事：新建员工前核验所选账号，以及员工名册补出登录名。
/// 账号是否存在、是否启用、是否属于本组织/环境是 IAM 的事实；「一个账号只关联一名员工」由 MasterData 唯一索引把关。
/// </summary>
internal static class BusinessConsoleWorkerLoginAccounts
{
    public const string AccountNotLinkableMessage = "所选登录账号不存在、已停用或不属于当前组织，请重新选择。";

    private const int IamBatchSize = 100;

    public static async Task EnsureLinkableAsync(
        IBusinessIamAccountDirectoryClient iamAccounts,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        string userId,
        CancellationToken cancellationToken)
    {
        var page = await iamAccounts.ListMemberAccountsAsync(
            internalBearerToken,
            new BusinessIamMemberAccountListRequest(
                organizationId,
                environmentId,
                UserIds: [userId],
                IncludeDisabled: false,
                PageIndex: 1,
                PageSize: 1),
            cancellationToken);
        var linkable = page.Items?.Any(account =>
            account.Enabled
            && string.Equals(account.UserId, userId, StringComparison.Ordinal)) == true;
        if (!linkable)
        {
            throw BusinessServiceProxyException.FromDownstreamBusinessMessage(AccountNotLinkableMessage);
        }
    }

    /// <summary>
    /// 登录名的可见范围（#3924，编排按最小权限裁定）：与「关联登录账号」目录同一道门——
    /// 持有 <c>masterdata.resources.manage</c> 且为组织级授权的主体才看得到。只读员工的主体
    /// （PDA 岗位、ERP 岗位等）拿不到登录名。鉴权本身失败按不可见处理（fail closed）。
    /// </summary>
    public static async Task<bool> CanSeeLoginNamesAsync(
        IBusinessGatewayAuthorizationClient auth,
        string bearerToken,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var definition = BusinessConsoleSearchableDirectoryPolicy.Require("login-account");
        BusinessGatewayAuthorizationResult authorization;
        try
        {
            authorization = await auth.CheckAsync(
                bearerToken,
                new BusinessGatewayPermissionRequirement(
                    definition.PermissionCode,
                    organizationId,
                    environmentId,
                    "organization",
                    organizationId,
                    IncludePrincipalContext: true),
                BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }

        return BusinessConsoleSearchableDirectoryPolicy.ResolveAuthorizedScope(
            definition,
            authorization,
            organizationId,
            null,
            null) is not null;
    }

    /// <summary>
    /// 给名册里的每名员工补出关联账号的登录名，并把响应标记为 <c>LoginNamesVisible</c>。只认本组织/环境的成员账号
    /// （含已停用账号，名册要如实显示）；查不到的留空，前端显示为「未关联」。
    /// IAM 读失败时不拖垮名册：原样返回、不标记可见，前端显示「—」而不是「未关联」。
    /// </summary>
    public static async Task<BusinessConsoleWorkerDirectoryResponse> AttachLoginNamesAsync(
        IBusinessIamAccountDirectoryClient iamAccounts,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        BusinessConsoleWorkerDirectoryResponse workers,
        CancellationToken cancellationToken)
    {
        var userIds = workers.Items
            .Select(item => item.UserId)
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var loginNames = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var batch in userIds.Chunk(IamBatchSize))
            {
                var page = await iamAccounts.ListMemberAccountsAsync(
                    internalBearerToken,
                    new BusinessIamMemberAccountListRequest(
                        organizationId,
                        environmentId,
                        UserIds: batch,
                        IncludeDisabled: true,
                        PageIndex: 1,
                        PageSize: batch.Length),
                    cancellationToken);
                foreach (var account in page.Items ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(account.UserId) && !string.IsNullOrWhiteSpace(account.LoginName))
                    {
                        loginNames[account.UserId] = account.LoginName;
                    }
                }
            }
        }
        catch (BusinessServiceProxyException)
        {
            return workers;
        }

        return workers with
        {
            LoginNamesVisible = true,
            Items = workers.Items
                .Select(item => item with
                {
                    LoginName = loginNames.TryGetValue(item.UserId, out var loginName) ? loginName : null,
                })
                .ToArray(),
        };
    }
}
