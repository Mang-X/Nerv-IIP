using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Commands;

/// <summary>
/// 维修工单正式单号（#3852）：四个开单入口（手工 / 报警建单、计划到期、点检不合格）统一由编码规则
/// <c>maintenance-work-order</c> 分配（MWO-yyyyMMdd-NNNNNN），不再由前端从 GUID 截取冒充。
/// <para>
/// 带意图键时，「键 → 号」绑定在独立 scope 里当场提交（<see cref="MaintenanceCodingService.AllocateWithCommittedBindingAsync"/>），
/// 外层事务回滚后同一意图重试拿回同一个号；没有意图键（手工建单未带幂等键）时每次都是新号。
/// </para>
/// <para>
/// 意图键一律是「类别前缀 + 原值的 SHA-256 十六进制串（64 字符）」，定长、不超过绑定表
/// <c>idempotency_key</c> 的 150 列宽——报警 ID、幂等键都来自外部，各自最长 150，直接拼接会超长。
/// </para>
/// </summary>
public static class MaintenanceWorkOrderNumbers
{
    public const string RuleKey = "maintenance-work-order";

    /// <summary>建单入口的意图键：报警建单按报警去重，带幂等键的手工建单按幂等键；否则不设（每次新号）。</summary>
    public static string? CreateIntent(string? sourceAlarmId, string? idempotencyKey) =>
        !string.IsNullOrWhiteSpace(sourceAlarmId)
            ? Intent("alarm", sourceAlarmId.Trim())
            : idempotencyKey is null ? null : Intent("create", idempotencyKey);

    /// <summary>计划到期生成的意图键：同一计划同一到期点只取一个号。</summary>
    public static string PlanIntent(string planCode, string dueSuffix) => Intent("plan", $"{planCode}:{dueSuffix}");

    /// <summary>点检不合格派生工单的意图键：同一条点检记录只取一个号。</summary>
    public static string InspectionIntent(string inspectionId) => Intent("inspection", inspectionId);

    private static string Intent(string kind, string rawValue) =>
        $"{kind}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawValue)))}";

    public static Task<string> AllocateAsync(
        MaintenanceCodingService codingService,
        string organizationId,
        string environmentId,
        string? intentKey,
        CancellationToken cancellationToken) =>
        codingService.AllocateWithCommittedBindingAsync(
            organizationId,
            environmentId,
            RuleKey,
            intentKey,
            MaintenanceCodingService.Fingerprint(RuleKey, intentKey),
            cancellationToken);
}
