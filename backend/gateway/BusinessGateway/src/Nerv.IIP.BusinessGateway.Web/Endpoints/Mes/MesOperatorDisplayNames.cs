using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints;

/// <summary>仅解析当前已授权 MES 响应引用的账号；IAM 保留姓名事实所有权。</summary>
internal static class MesOperatorDisplayNames
{
    // IAM MemberAccountListOptions 的 userIds 与 pageSize 上限均为 100。
    private const int BatchSize = 100;

    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IBusinessIamAccountDirectoryClient iamAccounts,
        string internalBearerToken,
        string organizationId,
        string environmentId,
        IEnumerable<string?> userIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = userIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var batch in references.Chunk(BatchSize))
        {
            var accounts = await iamAccounts.ListMemberAccountsAsync(
                internalBearerToken,
                new BusinessIamMemberAccountListRequest(
                    organizationId, environmentId, UserIds: batch, IncludeDisabled: true,
                    PageIndex: 1, PageSize: batch.Length),
                cancellationToken);
            foreach (var account in accounts.Items)
            {
                if (batch.Contains(account.UserId, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(account.DisplayName))
                {
                    names[account.UserId] = account.DisplayName.Trim();
                }
            }
        }
        return names;
    }
}
