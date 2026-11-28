namespace Nerv.IIP.Contracts.MasterData;

/// <summary>承载 Approval 收到的最终用户 IAM Bearer，由 IAM 校验。</summary>
public static class ApprovalDepartmentUserAuthorization
{
    public const string HeaderName = "X-Nerv-IIP-User-Authorization";
}
