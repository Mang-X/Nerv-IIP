using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Commands;

public sealed record MaintenanceCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class MaintenanceCodingService
{
    private readonly CodeAllocator _allocator;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    public MaintenanceCodingService()
    {
        _allocator = new CodeAllocator();
    }

    public MaintenanceCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));
    }

    /// <summary>
    /// 分配单号，并把「幂等键 → 单号」绑定**在独立 scope 里当场提交**，不随调用方的外层事务
    /// （与 <c>WmsCodingService</c> 同一做法，#3852 审核阻断 2）：外层回滚后同键重试一定拿回同一个号。
    /// 没有幂等键时每次都是新号。绑定同键并发撞车时按先提交者的结果重放。
    /// </summary>
    public async Task<string> AllocateWithCommittedBindingAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string? idempotencyKey,
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
            "Maintenance");
        if (_serviceScopeFactory is null)
        {
            return (await _allocator.AllocateAsync(request, cancellationToken)).Code;
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
            await using var replayScope = _serviceScopeFactory.CreateAsyncScope();
            var replayDbContext = replayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var replay = await NewAllocator(replayDbContext).TryPeekReplayAsync(request, cancellationToken);
            return replay?.Code ?? throw new CodeConcurrencyException("Maintenance code idempotency binding collided with another writer.");
        }
    }

    private CodeAllocator NewAllocator(ApplicationDbContext bindingDbContext) =>
        new(new EfCoreCodeStore(
            bindingDbContext,
            EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(_serviceScopeFactory!)));

    public async Task<MaintenanceCodeAllocation> AllocateAsync(
        string organizationId,
        string environmentId,
        string ruleKey,
        string? requestedCode,
        string? idempotencyKey,
        string payloadFingerprint,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var allocation = await _allocator.AllocateAsync(
            new CodeAllocationRequest(
                organizationId,
                environmentId,
                StandardCodeRules.Get(ruleKey),
                fields,
                requestedCode,
                idempotencyKey,
                payloadFingerprint,
                "Maintenance"),
            cancellationToken);

        return new MaintenanceCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return CodeAllocator.Fingerprint(parts);
    }
}
