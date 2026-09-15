using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 用例侧取 <see cref="ErpCodingService"/> 的入口（#3278 / S7）。
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>为什么不是每处 <c>new ErpCodingService()</c></b>：无参构造用的是**进程内**分配器，
/// 计数器挂在实例上。一个用例里构造两个 handler 各给一个新实例 ⇒ 两边都从 <c>000001</c> 起号 ⇒
/// 真库 lane 上撞 <c>(organization_id, environment_id, voucher_no)</c> 唯一索引（<c>23505</c>）。
/// 本入口按**数据库**分桶，同一个库上的所有 <see cref="DbContext"/> 实例共用一个分配器。
/// </para>
/// <para>
/// ⚠️ <b>失效方向</b>：这是**用例夹具**，不是生产装配。生产走 DI 的
/// <c>ErpCodingService(ApplicationDbContext, IServiceScopeFactory)</c>，计数器与幂等键都落库；
/// 本入口给出的是进程内分配器 ⇒ <c>IsIdempotentReplay</c> 跨 <see cref="DbContext"/> 实例仍成立（同一实例内缓存），
/// 但跨进程不成立。需要证明**落库的**幂等键行为时必须用 DI 装配
/// （见 <c>ErpCostAccountingPostgresAcceptanceTests.CreateErpPersistenceProvider</c>），⛔ 不要用本入口。
/// </para>
/// </remarks>
internal static class ErpTestCoding
{
    private static readonly ConcurrentDictionary<string, ErpCodingService> ByDatabase = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<DbContext, ErpCodingService> ByContext = new();

    public static ErpCodingService For(DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (dbContext.Database.IsRelational())
        {
            var connectionString = dbContext.Database.GetConnectionString();
            if (!string.IsNullOrEmpty(connectionString))
            {
                return ByDatabase.GetOrAdd(connectionString, _ => new ErpCodingService());
            }
        }

        // InMemory lane：按 DbContext 实例分桶。⛔ 不退回到进程级单例——那会把计数器变成跨用例的
        // 进程内全局状态，只跑过滤集时藏住、跑全程序集时才暴露。
        return ByContext.GetValue(dbContext, _ => new ErpCodingService());
    }
}

/// <summary>
/// 分配器凭证号形状断言（#3278 / S7）。
/// </summary>
/// <remarks>
/// ⚠️ 只钉**形状**，⛔ 不钉具体序号：序号来自计数器，同一程序集里跑到第几号取决于用例执行顺序，
/// 钉死序号等于把用例之间的执行顺序当契约。落到具体某张凭证上的断言请钉
/// <c>(SourceType, SourceNo)</c>（那才是 S5 之后的身份），凭证号只断言形状与互异。
/// </remarks>
internal static partial class AllocatedVoucherNo
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^JV-\d{8}-\d{6}$")]
    private static partial System.Text.RegularExpressions.Regex Shape();

    /// <summary><c>journal-voucher</c> 规则的产出形状：<c>JV-yyyyMMdd-NNNNNN</c>（定长 18）。</summary>
    public static bool Matches(string voucherNo) => voucherNo is not null && Shape().IsMatch(voucherNo);

    public static void AssertShape(string voucherNo)
    {
        Assert.True(
            Matches(voucherNo),
            $"Voucher number '{voucherNo}' is not the allocator shape JV-yyyyMMdd-NNNNNN.");
    }
}
