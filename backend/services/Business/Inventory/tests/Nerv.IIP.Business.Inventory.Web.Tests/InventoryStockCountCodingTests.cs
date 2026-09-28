using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Coding;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Coding;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// #3918：控制台手工新建盘点任务不传任务号，由编码规则（SCT）生成；同一次提交沿用同一个幂等键。
/// </summary>
public sealed class InventoryStockCountCodingTests
{
    private const string IntentKey = "console-count-task-intent-001";

    /// <summary>
    /// 盘点会冻结台账，本地落库失败后同键重试（新 scope）必须拿回同一个任务号，
    /// 不能一次提交在界面上变成两个号。
    /// </summary>
    [Fact]
    public async Task Count_task_without_a_code_gets_one_from_the_coding_rule_and_a_retry_in_a_new_scope_keeps_it()
    {
        await using var provider = CreateProvider();
        await SeedLedgerAsync(provider);
        var coding = new InventoryCodingService(provider.GetRequiredService<IServiceScopeFactory>());

        var lost = await AttemptAsync(provider, coding, Command("LOC-A-01"), commit: false);
        var retried = await AttemptAsync(provider, coding, Command("LOC-A-01"), commit: true);
        var replayed = await AttemptAsync(provider, coding, Command("LOC-A-01"), commit: true);

        Assert.Matches(@"^SCT-\d{8}-\d{6}$", retried.CountTaskCode);
        Assert.Equal(lost.CountTaskCode, retried.CountTaskCode);
        Assert.Equal(retried.CountTaskId, replayed.CountTaskId);
        Assert.Equal(retried.CountTaskCode, replayed.CountTaskCode);
        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = Assert.Single(await verifyDb.StockCountTasks.ToListAsync());
        Assert.Equal(retried.CountTaskCode, task.CountTaskCode);
    }

    /// <summary>
    /// 审核 B1：弹窗里第一次提交被业务校验拒绝（选到了没有台账的库位），用户在同一弹窗改正后
    /// 用同一个键重提必须成功，而且拿到的是第一个号——失败的请求既不能占号，也不能留下
    /// 一条会让改正后的载荷撞指纹冲突的绑定。
    /// </summary>
    [Fact]
    public async Task Rejected_attempt_neither_consumes_a_number_nor_blocks_the_corrected_retry_with_the_same_key()
    {
        await using var provider = CreateProvider();
        await SeedLedgerAsync(provider);
        var coding = new InventoryCodingService(provider.GetRequiredService<IServiceScopeFactory>());

        var rejected = await Assert.ThrowsAsync<KnownException>(() =>
            AttemptAsync(provider, coding, Command("LOC-NONE"), commit: true));
        var corrected = await AttemptAsync(provider, coding, Command("LOC-A-01"), commit: true);

        Assert.Equal("未找到盘点任务对应的库存台账。", rejected.Message);
        Assert.Matches(@"^SCT-\d{8}-000001$", corrected.CountTaskCode);
    }

    /// <summary>
    /// 审核 S1：同键并发时另一请求先提交了绑定，本请求提交绑定撞唯一约束，
    /// 必须按对方已提交的绑定重放拿回同一个号，而不是把并发冲突抛给用户。
    /// </summary>
    [Fact]
    public async Task Binding_collision_with_a_concurrent_writer_replays_the_winners_number()
    {
        const string winnerCode = "SCT-20260928-000042";
        var interceptor = new ConcurrentWinnerInterceptor();
        await using var provider = CreateProvider(interceptor);
        interceptor.CommitWinnerAsync = async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
                "org-001",
                "env-dev",
                InventoryCodeRules.StockCountTask,
                IntentKey,
                winnerCode,
                "same-payload",
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        };
        var coding = new InventoryCodingService(provider.GetRequiredService<IServiceScopeFactory>());

        var code = await coding.AllocateAsync(
            "org-001",
            "env-dev",
            InventoryCodeRules.StockCountTask,
            IntentKey,
            "same-payload",
            CancellationToken.None);

        Assert.True(interceptor.Fired);
        Assert.Equal(winnerCode, code);
    }

    private static CreateStockCountTaskCommand Command(string locationCode) => new(
        "org-001",
        "env-dev",
        null,
        "SKU-FG-1000",
        "kg",
        "SITE-01",
        locationCode,
        "LOT-001",
        null,
        "qualified",
        "company",
        "owner-001",
        IntentKey);

    private static async Task<CreateStockCountTaskResult> AttemptAsync(
        ServiceProvider provider,
        InventoryCodingService coding,
        CreateStockCountTaskCommand command,
        bool commit)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var result = await new CreateStockCountTaskCommandHandler(db, coding).Handle(command, CancellationToken.None);
        if (commit)
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return result;
    }

    private static async Task SeedLedgerAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledger = DomainLedgerFactory.NewLedger();
        ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
        db.StockLedgers.Add(ledger);
        await db.SaveChangesAsync();
    }

    private static ServiceProvider CreateProvider(IInterceptor? interceptor = null)
    {
        var databaseName = $"inventory-stock-count-coding-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
        });
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 内存库不执行唯一索引：在本请求提交绑定的那一刻先替「并发的另一请求」提交同键绑定，
    /// 再按真库的行为让本次提交以 <see cref="DbUpdateException"/> 失败。
    /// </summary>
    private sealed class ConcurrentWinnerInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? CommitWinnerAsync { get; set; }

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var bindsKey = eventData.Context!.ChangeTracker.Entries<CodeIdempotencyKey>()
                .Any(entry => entry.State == EntityState.Added);
            if (Fired || !bindsKey)
            {
                return result;
            }

            Fired = true;
            await CommitWinnerAsync!();
            throw new DbUpdateException("duplicate key value violates unique constraint \"ux_code_idempotency_keys_scope\"");
        }
    }
}
