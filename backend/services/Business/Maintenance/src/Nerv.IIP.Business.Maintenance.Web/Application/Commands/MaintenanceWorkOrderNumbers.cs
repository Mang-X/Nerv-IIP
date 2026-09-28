namespace Nerv.IIP.Business.Maintenance.Web.Application.Commands;

/// <summary>
/// 维修工单正式单号（#3852）：四个开单入口（手工 / 报警建单、计划到期、点检不合格）统一由编码规则
/// <c>maintenance-work-order</c> 分配（MWO-yyyyMMdd-NNNNNN），不再由前端从 GUID 截取冒充。
/// <para>
/// 分配在独立事务里提交计数器；传入的意图键让同一意图重试时拿回同一个号（外层事务回滚后重试不跳号），
/// 没有意图键（手工建单未带幂等键）时每次都是新号。
/// </para>
/// </summary>
public static class MaintenanceWorkOrderNumbers
{
    public const string RuleKey = "maintenance-work-order";

    /// <summary>建单入口的意图键：报警建单按报警去重，带幂等键的手工建单按幂等键；否则不设（每次新号）。</summary>
    public static string? CreateIntent(string? sourceAlarmId, string? idempotencyKey) =>
        !string.IsNullOrWhiteSpace(sourceAlarmId)
            ? $"alarm:{sourceAlarmId.Trim()}"
            : idempotencyKey is null ? null : $"create:{idempotencyKey}";

    public static async Task<string> AllocateAsync(
        MaintenanceCodingService codingService,
        string organizationId,
        string environmentId,
        string? intentKey,
        CancellationToken cancellationToken)
    {
        var allocation = await codingService.AllocateAsync(
            organizationId,
            environmentId,
            RuleKey,
            requestedCode: null,
            idempotencyKey: intentKey,
            payloadFingerprint: MaintenanceCodingService.Fingerprint(RuleKey, intentKey),
            cancellationToken);
        return allocation.Code;
    }
}
