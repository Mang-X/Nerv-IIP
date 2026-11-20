using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Coding;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

/// <summary>
/// 一次凭证号分配的结果。<see cref="Code"/> 为 <see langword="null"/> 表示**没拿到号**，
/// 调用方必须 gate-and-skip（写死信 + 不建凭证），⛔ 不得继续往下走。
/// </summary>
/// <param name="Code">分配到的凭证号；失败时为 <see langword="null"/>。</param>
/// <param name="FailureMessage">失败原因；成功时为空串。</param>
internal readonly record struct JournalVoucherNumberAllocation(string? Code, string FailureMessage);

/// <summary>集成事件消费侧的凭证取号入口（#3278 / S7）。</summary>
/// <remarks>
/// 不同事件可能指向同一张来源单据。按来源身份取号可以让这些事件复用同一号码。
/// <see cref="JournalVoucherNoAllocation"/> 负责分配和重放。本入口将
/// <see cref="CodeConcurrencyException"/> 和 <see cref="KnownException"/> 转成失败结果，
/// 由调用方写死信并跳过建凭证，避免业务失败无限重投（#877）。
/// 取消以及规则键缺失等编程错误继续向上传递。
/// </remarks>
internal static class ConsumerJournalVoucherNumber
{
    /// <summary><c>StandardCodeRules</c> 里记账凭证的规则键，产出 <c>JV-yyyyMMdd-NNNNNN</c>。</summary>
    public const string RuleKey = JournalVoucherNoAllocation.RuleKey;

    /// <summary>死信失败码：凭证号没分配下来，本次不建凭证。</summary>
    public const string AllocationFailureCode = "voucher-number-allocation-failed";

    /// <summary>取得与凭证分配请求相同的来源幂等键。</summary>
    public static string IdempotencyKeyOf(JournalVoucherSourceType sourceType, string sourceNo)
        => JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, sourceNo);

    public static async Task<JournalVoucherNumberAllocation> TryAllocateAsync(
        ErpCodingService codingService,
        string organizationId,
        string environmentId,
        JournalVoucherSourceType sourceType,
        string sourceNo,
        CancellationToken cancellationToken)
    {
        try
        {
            var code = await JournalVoucherNoAllocation.AllocateAsync(
                codingService,
                organizationId,
                environmentId,
                sourceType,
                sourceNo,
                cancellationToken);
            return new JournalVoucherNumberAllocation(code, string.Empty);
        }
        catch (Exception exception) when (exception is CodeConcurrencyException or KnownException)
        {
            return new JournalVoucherNumberAllocation(
                null,
                $"Journal voucher number could not be allocated for source '{sourceType.Code}'/'{sourceNo}': {exception.Message}");
        }
    }
}
