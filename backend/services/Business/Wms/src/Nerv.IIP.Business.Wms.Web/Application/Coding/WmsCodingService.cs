using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Business.Wms.Web.Application.Coding;

/// <summary>WMS 单号的编码规则键，规则本体见 <see cref="StandardCodeRules"/>。</summary>
public static class WmsCodeRules
{
    public const string InboundOrder = "wms-inbound-order";
    public const string OutboundOrder = "wms-outbound-order";
    public const string PutawayTask = "wms-putaway-task";
    public const string PickingTask = "wms-picking-task";
    public const string PackReview = "wms-pack-review";
    public const string CountExecution = "wms-count-execution";
    public const string WcsTask = "wms-wcs-task";
    public const string WorkPool = "wms-work-pool";
}

/// <summary>
/// WMS 单号分配：不传单号时按编码规则生成，传了（仅事件派生等内部调用）则原样沿用。
/// 同一幂等键重放拿回同一个单号，调用方随后按单号走原有的「已存在即返回」路径。
/// </summary>
public sealed class WmsCodingService
{
    private readonly CodeAllocator _allocator;

    public WmsCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public WmsCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    public async Task<string> AllocateAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var allocation = await _allocator.AllocateAsync(
            new CodeAllocationRequest(
                organizationId,
                environmentId,
                StandardCodeRules.Get(ruleKey),
                null,
                requestedCode,
                idempotencyKey,
                payloadFingerprint,
                "Wms"),
            cancellationToken);
        return allocation.Code;
    }

    public static string Fingerprint(params object?[] parts) => CodeAllocator.Fingerprint(parts);
}
