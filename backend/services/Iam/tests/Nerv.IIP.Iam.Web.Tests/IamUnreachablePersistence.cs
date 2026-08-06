using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// IAM 用例里「请求在触达持久化之前就被拒」这类断言所用的持久化配置。
///
/// 目标端点由 <see cref="NetworkFailureFixture.ReserveRefusedLoopbackEndpoint"/> 提供：一个刚绑定
/// 又立刻释放的本地端口。因此一旦回归让请求真的走到持久化，会立刻拿到
/// <see cref="NetworkFailureKind.ConnectionRefused"/>，而不是把 DNS 失败、连接被拒和超时混成
/// 「反正连不上」，也不依赖运行环境的解析器或防火墙碰巧让某个地址超时。
/// </summary>
internal static class IamUnreachablePersistence
{
    public static string ConnectionString()
    {
        return UnreachablePostgres.ConnectionRefusedConnectionString(
            NetworkFailureFixture.ReserveRefusedLoopbackEndpoint(),
            database: "nerv_iip_iam_unreachable",
            username: "nerv",
            password: "nerv");
    }
}
