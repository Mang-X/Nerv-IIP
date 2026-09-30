using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Business.Inventory.Web.Application.Coding;

/// <summary>库存单号的编码规则键，规则本体见 <see cref="StandardCodeRules"/>。</summary>
public static class InventoryCodeRules
{
    /// <summary>
    /// 库存手工新建的盘点任务（前缀 SCT）。WMS 下发的盘点任务沿用 WMS 盘点单号（前缀 CNT），
    /// 两者落同一张按单号唯一的表，前缀不同才不会撞号（#3918）。
    /// </summary>
    public const string StockCountTask = "inventory-stock-count-task";
}

/// <summary>
/// 库存单号分配：按编码规则生成。
///
/// **「幂等键 → 单号」的绑定在分配时就独立提交**，不随调用方的本地事务：盘点任务会冻结台账，
/// 若本地落库失败而绑定跟着回滚，同键重试会分到新单号，界面上同一次提交变成两个号。
/// 独立提交后，同键重试一定拿回同一个单号，调用方随后按幂等键走「已存在即返回」或继续补建。
/// </summary>
public sealed class InventoryCodingService
{
    private readonly CodeAllocator? _inMemoryAllocator;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    public InventoryCodingService()
    {
        _inMemoryAllocator = new CodeAllocator();
    }

    public InventoryCodingService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<string> AllocateAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken)
    {
        var request = new CodeAllocationRequest(
            organizationId,
            environmentId,
            StandardCodeRules.Get(ruleKey),
            null,
            null,
            idempotencyKey,
            payloadFingerprint,
            "Inventory");
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
            return replay?.Code ?? throw new CodeConcurrencyException("Inventory code idempotency binding collided with another writer.");
        }
    }

    public static string Fingerprint(params object?[] parts) => CodeAllocator.Fingerprint(parts);

    private CodeAllocator NewAllocator(ApplicationDbContext bindingDbContext) =>
        new(new EfCoreCodeStore(
            bindingDbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(_serviceScopeFactory!)));
}
