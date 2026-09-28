using Microsoft.EntityFrameworkCore;
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
///
/// **「幂等键 → 单号」的绑定在分配时就独立提交**，不随调用方的本地事务：拣货、盘点在本地落库前
/// 先做远程副作用（库存预留、冻结），预留键由单号派生（ADR 0031）。若绑定跟着本地事务回滚，
/// 同键重试会分到新单号、生成第二份预留，第一份成孤儿。独立提交后，同键重试一定拿回同一个单号，
/// 调用方随后按单号走「已存在即返回」或继续补建。
/// </summary>
public sealed class WmsCodingService
{
    private readonly CodeAllocator? _inMemoryAllocator;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    public WmsCodingService()
    {
        _inMemoryAllocator = new CodeAllocator();
    }

    public WmsCodingService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
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
        var request = new CodeAllocationRequest(
            organizationId,
            environmentId,
            StandardCodeRules.Get(ruleKey),
            null,
            requestedCode,
            idempotencyKey,
            payloadFingerprint,
            "Wms");
        if (_serviceScopeFactory is null)
        {
            return (await _inMemoryAllocator!.AllocateAsync(request, cancellationToken)).Code;
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var bindingDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var allocation = await NewAllocator(bindingDbContext).AllocateAsync(request, cancellationToken);
        if (allocation.IsIdempotentReplay || !bindingDbContext.ChangeTracker.HasChanges())
        {
            return allocation.Code;
        }

        try
        {
            await bindingDbContext.SaveChangesAsync(cancellationToken);
            return allocation.Code;
        }
        catch (DbUpdateException)
        {
            // 同键并发：另一请求先提交了绑定，按它的结果重放（载荷不一致时分配器照常拒绝）。
            await using var replayScope = _serviceScopeFactory.CreateAsyncScope();
            var replayDbContext = replayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var replay = await NewAllocator(replayDbContext).TryPeekReplayAsync(request, cancellationToken);
            return replay?.Code ?? throw new CodeConcurrencyException("WMS code idempotency binding collided with another writer.");
        }
    }

    public static string Fingerprint(params object?[] parts) => CodeAllocator.Fingerprint(parts);

    private CodeAllocator NewAllocator(ApplicationDbContext bindingDbContext) =>
        new(new EfCoreCodeStore(
            bindingDbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(_serviceScopeFactory!)));
}
