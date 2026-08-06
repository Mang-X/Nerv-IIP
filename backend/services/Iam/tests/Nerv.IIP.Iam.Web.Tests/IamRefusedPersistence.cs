using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// IAM 用例里「请求在触达持久化之前就被拒」这类断言所用的持久化配置。
///
/// 目标端点由 <see cref="NetworkFailureFixture.ReserveRefusedLoopbackEndpoint"/> 提供。这些用例的
/// 被测意图是 <b>persistence 根本没被触达</b>，因此这里**没有**、也不应该有 <see cref="NetworkFailureKind"/>
/// 断言：一次成功的运行里连接尝试压根不会发生，套一句分类断言只会断言一个不存在的事件。
/// 这个连接串的作用是**护栏**而非被测对象 —— 一旦回归让请求真的走到持久化，会立刻拿到
/// <see cref="NetworkFailureKind.ConnectionRefused"/> 而挂掉，而不是把 DNS 失败、连接被拒和超时
/// 混成「反正连不上」，也不依赖运行环境的解析器或防火墙碰巧让某个地址超时。
/// </summary>
internal static class IamRefusedPersistence
{
    // 两档预算在此集中说明，三个调用点共享同一份理由。connect 预算按「本地 loopback 立即 RST」取小：
    // 它只是防呆的停滞上限，不是预期等待。request 预算取秒级并**刻意大于** connect 预算：它约束的是
    // 连接建立之后的单条命令，对着被拒端点永远不可能被触发，但一旦回归让该主机变得可达，命令就该按
    // 真实依赖的正常抖动来兜底，而不是继承一个为 loopback RST 挑的小数字。
    private static readonly TimeSpan ConnectBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(10);

    public static string ConnectionString()
    {
        return RefusedPostgres.ConnectionString(
            NetworkFailureFixture.ReserveRefusedLoopbackEndpoint(),
            database: "nerv_iip_iam_unreachable",
            username: "nerv",
            password: "nerv",
            connectBudget: ConnectBudget,
            requestBudget: RequestBudget);
    }
}
