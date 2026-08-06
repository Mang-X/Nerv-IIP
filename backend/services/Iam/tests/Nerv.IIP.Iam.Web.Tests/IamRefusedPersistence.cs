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
    public static string ConnectionString()
    {
        // 两档预算取共享的具名 preset，理由集中在 RefusedPostgresBudgets.RefusedLoopback 一处。
        return RefusedPostgres.ConnectionString(
            NetworkFailureFixture.ReserveRefusedLoopbackEndpoint(),
            database: "nerv_iip_iam_unreachable",
            username: "nerv",
            password: "nerv",
            RefusedPostgresBudgets.RefusedLoopback);
    }
}
